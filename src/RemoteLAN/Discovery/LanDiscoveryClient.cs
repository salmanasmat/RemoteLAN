using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using RemoteLAN.Protocol.Discovery;

namespace RemoteLAN.Discovery;

public sealed class LanDiscoveryClient
{
    private readonly int _discoveryPort;

    public LanDiscoveryClient(int discoveryPort = DiscoveryConstants.DiscoveryPort)
    {
        _discoveryPort = discoveryPort;
    }

    public async Task<IReadOnlyList<DiscoveredAgent>> DiscoverAgentsAsync(
        TimeSpan? timeout = null, 
        bool filterSelf = false, 
        CancellationToken cancellationToken = default)
    {
        TimeSpan scanTimeout = timeout ?? TimeSpan.FromMilliseconds(1500);
        var discoveredMap = new Dictionary<string, DiscoveredAgent>(StringComparer.OrdinalIgnoreCase);

        var localIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost", "::1" };
        if (filterSelf)
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var unicast in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            localIps.Add(unicast.Address.ToString());
                        }
                    }
                }
            }
            catch { }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(scanTimeout);

        using var udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;
        udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        byte[] requestBytes = Encoding.UTF8.GetBytes(DiscoveryConstants.DiscoveryRequestToken);

        // Send to general broadcast
        try
        {
            await udpClient.SendAsync(requestBytes, requestBytes.Length, new IPEndPoint(IPAddress.Broadcast, _discoveryPort)).ConfigureAwait(false);
            
            // Send to loopback only if not filtering self (e.g. for developer unit tests)
            if (!filterSelf)
            {
                await udpClient.SendAsync(requestBytes, requestBytes.Length, new IPEndPoint(IPAddress.Loopback, _discoveryPort)).ConfigureAwait(false);
            }
        }
        catch { }

        // Send directed broadcasts across all active IPv4 network adapters
        foreach (var broadcastAddress in GetSubnetBroadcastAddresses())
        {
            try
            {
                await udpClient.SendAsync(requestBytes, requestBytes.Length, new IPEndPoint(broadcastAddress, _discoveryPort)).ConfigureAwait(false);
            }
            catch { }
        }

        // Receive responses until timeout
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var result = await udpClient.ReceiveAsync(cts.Token).ConfigureAwait(false);
                string responseText = Encoding.UTF8.GetString(result.Buffer);
                string senderIp = result.RemoteEndPoint.Address.ToString();

                if (DiscoveredAgent.TryParse(responseText, senderIp, out var agent) && agent != null)
                {
                    if (filterSelf)
                    {
                        // 1. Ignore loopback or any local interface IP
                        if (IPAddress.IsLoopback(result.RemoteEndPoint.Address) || 
                            localIps.Contains(senderIp) || 
                            localIps.Contains(agent.IpAddress))
                        {
                            continue;
                        }

                        // 2. Ignore local machine name
                        if (string.Equals(agent.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    string key = $"{agent.IpAddress}:{agent.Port}";
                    discoveredMap[key] = agent;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when scan timeout expires
        }
        catch
        {
            // Ignore socket read errors during scan
        }

        // Deduplicate agents by MachineName so multi-homed devices (e.g. WiFi and Ethernet) are not listed redundantly
        return discoveredMap.Values
            .GroupBy(a => a.MachineName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.MachineName)
            .ToList();
    }

    private static IEnumerable<IPAddress> GetSubnetBroadcastAddresses()
    {
        var addresses = new List<IPAddress>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var unicast in ni.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask != null)
                    {
                        byte[] ipBytes = unicast.Address.GetAddressBytes();
                        byte[] maskBytes = unicast.IPv4Mask.GetAddressBytes();
                        byte[] broadcastBytes = new byte[4];

                        for (int i = 0; i < 4; i++)
                        {
                            broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                        }

                        addresses.Add(new IPAddress(broadcastBytes));
                    }
                }
            }
        }
        catch { }
        return addresses;
    }
}

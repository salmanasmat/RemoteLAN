using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using RemoteLAN.Protocol.Discovery;

namespace RemoteLAN.Discovery;

public sealed class AgentDiscoveryResponder : IDisposable
{
    private readonly int _tcpPort;
    private readonly int _discoveryPort;
    private readonly string _machineId;
    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public AgentDiscoveryResponder(int tcpPort, int discoveryPort = DiscoveryConstants.DiscoveryPort, string? machineId = null)
    {
        _tcpPort = tcpPort;
        _discoveryPort = discoveryPort;
        _machineId = !string.IsNullOrWhiteSpace(machineId) ? machineId : AgentIdentity.GetOrCreateMachineId();
    }

    public void Start()
    {
        if (_socket != null) return;

        try
        {
            _cts = new CancellationTokenSource();
            
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            try
            {
                _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            }
            catch
            {
                // PacketInformation option not supported on all platforms/adapters; fallback will resolve via subnet
            }

            _socket.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));

            _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        }
        catch (Exception)
        {
            // If discovery port cannot be bound, agent still functions normally on direct TCP
            Stop();
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        byte[] buffer = new byte[2048];

        while (!ct.IsCancellationRequested && _socket != null)
        {
            try
            {
                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                SocketReceiveMessageFromResult receiveResult;

                try
                {
                    receiveResult = await _socket.ReceiveMessageFromAsync(
                        new ArraySegment<byte>(buffer),
                        SocketFlags.None,
                        remoteEndPoint).WaitAsync(ct).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    // Fallback to standard ReceiveFromAsync if ReceiveMessageFromAsync fails
                    var standardResult = await _socket.ReceiveFromAsync(
                        new ArraySegment<byte>(buffer),
                        SocketFlags.None,
                        remoteEndPoint).WaitAsync(ct).ConfigureAwait(false);

                    receiveResult = new SocketReceiveMessageFromResult
                    {
                        ReceivedBytes = standardResult.ReceivedBytes,
                        RemoteEndPoint = standardResult.RemoteEndPoint,
                        PacketInformation = default
                    };
                }

                if (receiveResult.ReceivedBytes <= 0) continue;

                string message = Encoding.UTF8.GetString(buffer, 0, receiveResult.ReceivedBytes);

                if (string.Equals(message.Trim(), DiscoveryConstants.DiscoveryRequestToken, StringComparison.Ordinal))
                {
                    var remoteIp = (receiveResult.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.Loopback;
                    int ifIndex = receiveResult.PacketInformation.Interface;

                    var (interfaceType, adapterIp) = ResolveInterfaceInfo(ifIndex, remoteIp);

                    string responseText = $"{DiscoveryConstants.DiscoveryResponsePrefix}{Environment.MachineName}|{_tcpPort}|{DiscoveryConstants.CurrentProtocolVersion}|{_machineId}|{interfaceType}|{adapterIp}";
                    byte[] responseBytes = Encoding.UTF8.GetBytes(responseText);

                    await _socket.SendToAsync(
                        new ArraySegment<byte>(responseBytes),
                        SocketFlags.None,
                        receiveResult.RemoteEndPoint).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (ct.IsCancellationRequested) break;
            }
        }
    }

    public static (string interfaceType, string adapterIp) ResolveInterfaceInfo(int ifIndex, IPAddress remoteIp)
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            // 1. If we have a valid interface index from IP_PKTINFO, match it
            if (ifIndex > 0)
            {
                var match = interfaces.FirstOrDefault(n =>
                {
                    try
                    {
                        return n.Supports(NetworkInterfaceComponent.IPv4) &&
                               n.GetIPProperties().GetIPv4Properties()?.Index == ifIndex;
                    }
                    catch { return false; }
                });

                if (match != null)
                {
                    string ifType = GetInterfaceTypeString(match);
                    string ip = match.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString()
                        ?? string.Empty;

                    if (!string.IsNullOrEmpty(ip))
                    {
                        return (ifType, ip);
                    }
                }
            }

            // 2. Fallback: match local interface on the same subnet as the remote sender
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var unicast in ni.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask != null)
                    {
                        if (IsInSameSubnet(unicast.Address, remoteIp, unicast.IPv4Mask))
                        {
                            return (GetInterfaceTypeString(ni), unicast.Address.ToString());
                        }
                    }
                }
            }

            // 3. Fallback for loopback testing
            if (IPAddress.IsLoopback(remoteIp))
            {
                return ("Ethernet", "127.0.0.1");
            }
        }
        catch { }

        return ("Ethernet", string.Empty);
    }

    private static string GetInterfaceTypeString(NetworkInterface ni)
    {
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            return "WiFi";

        string desc = (ni.Name + " " + ni.Description).ToLowerInvariant();
        if (desc.Contains("wi-fi") || desc.Contains("wifi") || desc.Contains("wireless") || desc.Contains("802.11") || desc.Contains("wlan"))
            return "WiFi";

        return "Ethernet";
    }

    private static bool IsInSameSubnet(IPAddress ip1, IPAddress ip2, IPAddress mask)
    {
        byte[] b1 = ip1.GetAddressBytes();
        byte[] b2 = ip2.GetAddressBytes();
        byte[] m = mask.GetAddressBytes();

        if (b1.Length != 4 || b2.Length != 4 || m.Length != 4) return false;

        for (int i = 0; i < 4; i++)
        {
            if ((b1[i] & m[i]) != (b2[i] & m[i])) return false;
        }
        return true;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        try
        {
            _socket?.Dispose();
        }
        catch { }
        _socket = null;
    }

    public void Dispose()
    {
        Stop();
    }
}

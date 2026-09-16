using System.Net;
using System.Net.Sockets;
using System.Text;
using RemoteLAN.Protocol.Discovery;

namespace RemoteLAN.Agent.Discovery;

public sealed class AgentDiscoveryResponder : IDisposable
{
    private readonly int _tcpPort;
    private readonly int _discoveryPort;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public AgentDiscoveryResponder(int tcpPort, int discoveryPort = DiscoveryConstants.DiscoveryPort)
    {
        _tcpPort = tcpPort;
        _discoveryPort = discoveryPort;
    }

    public void Start()
    {
        if (_udpClient != null) return;

        try
        {
            _cts = new CancellationTokenSource();
            
            // Allow multiple listeners on same machine if needed
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));

            _udpClient = new UdpClient { Client = socket };
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
        while (!ct.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct).ConfigureAwait(false);
                string message = Encoding.UTF8.GetString(result.Buffer);

                if (string.Equals(message.Trim(), DiscoveryConstants.DiscoveryRequestToken, StringComparison.Ordinal))
                {
                    string responseText = $"{DiscoveryConstants.DiscoveryResponsePrefix}{Environment.MachineName}|{_tcpPort}|{DiscoveryConstants.CurrentProtocolVersion}";
                    byte[] responseBytes = Encoding.UTF8.GetBytes(responseText);

                    await _udpClient.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint).ConfigureAwait(false);
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

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        try
        {
            _udpClient?.Dispose();
        }
        catch { }
        _udpClient = null;
    }

    public void Dispose()
    {
        Stop();
    }
}

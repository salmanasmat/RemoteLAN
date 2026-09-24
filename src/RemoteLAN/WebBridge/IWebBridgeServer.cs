namespace RemoteLAN.WebBridge;

public interface IWebBridgeServer : IDisposable
{
    int Port { get; }
    bool IsRunning { get; }
    string? CertificateThumbprint { get; }
    
    event Action<string>? StatusChanged;
    event Action<string>? ClientConnected;
    event Action? ClientDisconnected;

    void DisconnectSession();

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}

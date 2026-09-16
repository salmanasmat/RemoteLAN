using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using RemoteLAN.Capture;
using RemoteLAN.Discovery;
using RemoteLAN.Input;
using RemoteLAN.Security;
using RemoteLAN.Protocol.Messages;
using RemoteLAN.Protocol.Transport;

namespace RemoteLAN.Network;

public sealed class AgentServer : IDisposable
{
    private readonly int _port;
    private readonly PinManager _pinManager;
    private readonly IScreenCapturer _capturer;
    private readonly JpegFrameEncoder _encoder;
    private readonly InputInjector _inputInjector;
    private readonly NetworkFrameWriter _writer;
    private readonly AgentDiscoveryResponder _discoveryResponder;

    private TcpListener? _listener;
    private CancellationTokenSource? _serverCts;
    private Task? _listenerTask;
    private TcpClient? _currentClient;
    private readonly object _clientLock = new();

    public int Port => _port;
    public string CaptureEngineName => _capturer.EngineName;
    public int ScreenWidth => _capturer.Width;
    public int ScreenHeight => _capturer.Height;
    public bool IsClientConnected => _currentClient?.Connected == true;

    public event Action<string>? StatusChanged;
    public event Action<string>? ClientConnected;
    public event Action? ClientDisconnected;
    public event Action<double>? FpsUpdated;

    public AgentServer(int port = ProtocolConstants.DefaultPort, int jpegQuality = 70, string? initialPin = null, int discoveryPort = RemoteLAN.Protocol.Discovery.DiscoveryConstants.DiscoveryPort, bool unattendedAccessEnabled = false, string? unattendedPassword = null)
    {
        _port = port;
        _pinManager = new PinManager(initialPin, unattendedAccessEnabled, unattendedPassword);
        _capturer = new ScreenCapturer();
        _encoder = new JpegFrameEncoder(jpegQuality);
        _inputInjector = new InputInjector();
        _writer = new NetworkFrameWriter();
        _discoveryResponder = new AgentDiscoveryResponder(_port, discoveryPort);
    }

    public PinManager PinManager => _pinManager;

    public void Start()
    {
        if (_listener != null) return;

        _serverCts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _discoveryResponder.Start();

        StatusChanged?.Invoke("Ready");
        _listenerTask = Task.Run(() => AcceptConnectionsAsync(_serverCts.Token));
    }

    public void Stop()
    {
        _serverCts?.Cancel();
        _discoveryResponder.Stop();

        lock (_clientLock)
        {
            _currentClient?.Close();
            _currentClient = null;
        }

        try
        {
            _listener?.Stop();
        }
        catch { }
        _listener = null;

        StatusChanged?.Invoke("Stopped");
    }

    public void DisconnectCurrentClient()
    {
        lock (_clientLock)
        {
            if (_currentClient != null)
            {
                _currentClient.Close();
                _currentClient = null;
            }
        }
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                client.NoDelay = true;
                client.ReceiveBufferSize = 65536;
                client.SendBufferSize = 131072;

                string clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";

                lock (_clientLock)
                {
                    if (_currentClient != null && _currentClient.Connected)
                    {
                        // Already serving a client; reject additional concurrent connections
                        client.Close();
                        continue;
                    }
                    _currentClient = client;
                }

                StatusChanged?.Invoke($"Client connecting from {clientEndpoint}...");

                // Process client session
                _ = Task.Run(() => HandleClientSessionAsync(client, clientEndpoint, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) break;
                StatusChanged?.Invoke($"Listener error: {ex.Message}");
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientSessionAsync(TcpClient client, string endpoint, CancellationToken serverCt)
    {
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
        var ct = sessionCts.Token;
        var networkStream = client.GetStream();

        try
        {
            // Phase 3 — Authentication Handshake
            var (type, length) = await NetworkFrameReader.ReadFrameHeaderAsync(networkStream, ct).ConfigureAwait(false);
            if (type != MessageType.AuthRequest)
            {
                var rejectResp = new AuthResponse
                {
                    Success = false,
                    Message = "Expected AuthRequest header."
                };
                await _writer.WriteFrameAsync(networkStream, MessageType.AuthResponse, rejectResp.Serialize(), ct).ConfigureAwait(false);
                client.Close();
                return;
            }

            byte[] authPayload = await NetworkFrameReader.ReadPayloadAsync(networkStream, length, ct).ConfigureAwait(false);
            var authReq = AuthRequest.Deserialize(authPayload);

            if (!_pinManager.ValidatePin(authReq.Pin))
            {
                var failResp = new AuthResponse
                {
                    Success = false,
                    Message = "Invalid PIN."
                };
                await _writer.WriteFrameAsync(networkStream, MessageType.AuthResponse, failResp.Serialize(), ct).ConfigureAwait(false);
                client.Close();
                StatusChanged?.Invoke($"Rejected connection from {endpoint} (Incorrect PIN)");
                return;
            }

            // PIN Verified! Send success response with screen geometry
            var successResp = new AuthResponse
            {
                Success = true,
                Message = "Authenticated successfully.",
                ScreenWidth = _capturer.Width,
                ScreenHeight = _capturer.Height
            };
            await _writer.WriteFrameAsync(networkStream, MessageType.AuthResponse, successResp.Serialize(), ct).ConfigureAwait(false);

            StatusChanged?.Invoke($"Connected to {endpoint}");
            ClientConnected?.Invoke(endpoint);

            // Run Video Streaming and Input Receiving concurrently
            var streamTask = Task.Run(() => StreamScreenLoopAsync(networkStream, sessionCts), ct);
            var inputTask = Task.Run(() => ReceiveInputLoopAsync(networkStream, sessionCts), ct);

            await Task.WhenAny(streamTask, inputTask).ConfigureAwait(false);
            sessionCts.Cancel();
        }
        catch (Exception ex) when (ex is IOException or SocketException or EndOfStreamException or OperationCanceledException)
        {
            // Expected on disconnect
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Session error: {ex.Message}");
        }
        finally
        {
            lock (_clientLock)
            {
                if (ReferenceEquals(_currentClient, client))
                {
                    _currentClient = null;
                }
            }

            client.Close();
            FpsUpdated?.Invoke(0.0);
            ClientDisconnected?.Invoke();
            StatusChanged?.Invoke("Ready");
        }
    }

    private async Task StreamScreenLoopAsync(NetworkStream stream, CancellationTokenSource sessionCts)
    {
        var ct = sessionCts.Token;
        const int targetFps = 15;
        TimeSpan targetFrameInterval = TimeSpan.FromMilliseconds(1000.0 / targetFps);

        int frameCount = 0;
        var fpsTimer = Stopwatch.StartNew();
        var frameIntervalTimer = new Stopwatch();

        while (!ct.IsCancellationRequested)
        {
            frameIntervalTimer.Restart();

            try
            {
                using var bitmap = _capturer.CaptureFrame();
                if (bitmap != null)
                {
                    byte[] jpegData = _encoder.Encode(bitmap);
                    await _writer.WriteFrameAsync(stream, MessageType.ScreenFrame, jpegData, ct).ConfigureAwait(false);
                    frameCount++;
                }

                if (fpsTimer.ElapsedMilliseconds >= 1000)
                {
                    double currentFps = frameCount * 1000.0 / fpsTimer.ElapsedMilliseconds;
                    FpsUpdated?.Invoke(Math.Round(currentFps, 1));
                    frameCount = 0;
                    fpsTimer.Restart();
                }

                TimeSpan elapsed = frameIntervalTimer.Elapsed;
                if (elapsed < targetFrameInterval)
                {
                    await Task.Delay(targetFrameInterval - elapsed, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                sessionCts.Cancel();
                break;
            }
        }
    }

    private async Task ReceiveInputLoopAsync(NetworkStream stream, CancellationTokenSource sessionCts)
    {
        var ct = sessionCts.Token;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (type, length) = await NetworkFrameReader.ReadFrameHeaderAsync(stream, ct).ConfigureAwait(false);
                byte[] payload = await NetworkFrameReader.ReadPayloadAsync(stream, length, ct).ConfigureAwait(false);

                switch (type)
                {
                    case MessageType.MouseMove:
                        var move = MouseMoveMessage.Deserialize(payload);
                        _inputInjector.InjectMouseMove(move.NormalizedX, move.NormalizedY);
                        break;

                    case MessageType.MouseButton:
                        var btn = MouseButtonMessage.Deserialize(payload);
                        _inputInjector.InjectMouseButton(btn.Button, btn.Action);
                        break;

                    case MessageType.MouseWheel:
                        var wheel = MouseWheelMessage.Deserialize(payload);
                        _inputInjector.InjectMouseWheel(wheel.Delta);
                        break;

                    case MessageType.KeyboardKey:
                        var key = KeyboardKeyMessage.Deserialize(payload);
                        _inputInjector.InjectKeyboardKey(key.VirtualKeyCode, key.Action, key.IsExtendedKey);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                sessionCts.Cancel();
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _discoveryResponder.Dispose();
        _capturer.Dispose();
    }
}

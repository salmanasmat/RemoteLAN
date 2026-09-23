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
using RemoteLAN.Power;

namespace RemoteLAN.Network;

public sealed class AgentServer : IDisposable
{
    private readonly int _port;
    private readonly SettingsManager? _settingsManager;
    private readonly PinManager _pinManager;
    private readonly IScreenCapturer _capturer;
    private readonly JpegFrameEncoder _encoder;
    private readonly IInputInjector _inputInjector;
    private readonly NetworkFrameWriter _writer;
    private readonly AgentDiscoveryResponder _discoveryResponder;

    private TcpListener? _listener;
    private CancellationTokenSource? _serverCts;
    private Task? _listenerTask;
    private TcpClient? _currentClient;
    private readonly object _clientLock = new();
    private int _sessionGeneration;
    private NetworkStream? _sessionStream;
    private readonly object _sessionStreamLock = new();

    public int Port => _port;
    public SettingsManager? SettingsManager => _settingsManager;
    public string CaptureEngineName => _capturer.EngineName;
    public int ScreenWidth => _capturer.Width;
    public int ScreenHeight => _capturer.Height;
    public bool IsClientConnected => _currentClient?.Connected == true;

    public event Action<string>? StatusChanged;
    public event Action<string>? ClientConnected;
    public event Action? ClientDisconnected;
    public event Action<double>? FpsUpdated;
    public event Action<IncomingConnectionEventArgs>? IncomingConnectionRequested;
    public event Action? IncomingConnectionDismissed;
    public event Action<ChatMessagePayload>? ChatMessageReceived;
    public event Action? ControllerTypingStarted;
    public event Action? ChatSessionEnded;

    public PinManager PinManager => _pinManager;

    private bool _unattendedKeepAwakeAcquired;

    public AgentServer(
        int port = ProtocolConstants.DefaultPort, 
        int jpegQuality = 70, 
        string? initialPin = null, 
        int discoveryPort = RemoteLAN.Protocol.Discovery.DiscoveryConstants.DiscoveryPort, 
        bool unattendedAccessEnabled = false, 
        string? unattendedPassword = null,
        SettingsManager? settingsManager = null,
        IInputInjector? inputInjector = null)
    {
        _port = port;
        _settingsManager = settingsManager;
        _pinManager = new PinManager(initialPin, unattendedAccessEnabled, unattendedPassword);
        _capturer = new ScreenCapturer();
        _encoder = new JpegFrameEncoder(jpegQuality);
        _inputInjector = inputInjector ?? new InputInjector();
        _writer = new NetworkFrameWriter();
        _discoveryResponder = new AgentDiscoveryResponder(_port, discoveryPort);

        _pinManager.UnattendedAccessChanged += UpdateUnattendedPowerState;
        if (unattendedAccessEnabled)
        {
            UpdateUnattendedPowerState(true, unattendedPassword);
        }
    }

    private void UpdateUnattendedPowerState(bool enabled, string? _)
    {
        if (enabled && !_unattendedKeepAwakeAcquired)
        {
            _unattendedKeepAwakeAcquired = true;
            SystemPowerManager.AcquireKeepAwake();
        }
        else if (!enabled && _unattendedKeepAwakeAcquired)
        {
            _unattendedKeepAwakeAcquired = false;
            SystemPowerManager.ReleaseKeepAwake();
        }
    }

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
        _inputInjector.ResetSession(0);

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
        _inputInjector.ResetSession(0);
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

            string clientIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? endpoint;

            if (_settingsManager != null && _settingsManager.IsIpLockedOut(clientIp, out var remainingTime))
            {
                int remainingMinutes = Math.Max(1, (int)Math.Ceiling(remainingTime.TotalMinutes));
                var lockedResp = new AuthResponse
                {
                    Success = false,
                    Message = $"Access blocked due to multiple failed attempts. Try again in {remainingMinutes} minute(s)."
                };
                await _writer.WriteFrameAsync(networkStream, MessageType.AuthResponse, lockedResp.Serialize(), ct).ConfigureAwait(false);
                client.Close();
                StatusChanged?.Invoke($"Blocked unauthorized connection from {clientIp} (Locked out for {remainingMinutes} min)");
                return;
            }

            bool authenticated = false;
            string clientMachineName = string.IsNullOrWhiteSpace(authReq.ClientMachineName) ? clientIp : authReq.ClientMachineName;

            if (!string.IsNullOrEmpty(authReq.Pin))
            {
                if (_pinManager.ValidatePin(authReq.Pin))
                {
                    authenticated = true;
                }
                else
                {
                    _settingsManager?.RecordFailedAttempt(clientIp);
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
            }
            else
            {
                // If the host is currently at the Windows sign-in / lock screen, no interactive user is logged in
                // on winsta0\default to click "Accept". Immediately inform the viewer to supply the Host PIN or unattended password.
                if (DesktopManager.IsLockScreenActive())
                {
                    var lockResp = new AuthResponse
                    {
                        Success = false,
                        Message = "Remote host is currently at the Windows sign-in screen. Please enter the Host PIN or Unattended Password to connect."
                    };
                    await _writer.WriteFrameAsync(networkStream, MessageType.AuthResponse, lockResp.Serialize(), ct).ConfigureAwait(false);
                    client.Close();
                    StatusChanged?.Invoke($"Declined unauthenticated connection from {endpoint}: Host is at lock screen, PIN required.");
                    return;
                }

                // Client connected with empty PIN: request approval from remote user on this PC
                var tcsApproval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var reqArgs = new IncomingConnectionEventArgs(endpoint, clientIp, clientMachineName, tcsApproval);

                try
                {
                    IncomingConnectionRequested?.Invoke(reqArgs);
                    StatusChanged?.Invoke($"Incoming connection request from {clientMachineName} ({clientIp})...");

                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                    // Watch for client disconnect
                    var pollTask = Task.Run(async () =>
                    {
                        try
                        {
                            while (!linkedCts.Token.IsCancellationRequested)
                            {
                                await Task.Delay(300, linkedCts.Token).ConfigureAwait(false);
                                if (!client.Connected || (client.Client.Poll(1000, SelectMode.SelectRead) && client.Client.Available == 0))
                                {
                                    tcsApproval.TrySetCanceled();
                                    break;
                                }
                            }
                        }
                        catch { }
                    }, linkedCts.Token);

                    bool accepted = false;
                    try
                    {
                        using (linkedCts.Token.Register(() => tcsApproval.TrySetCanceled()))
                        {
                            accepted = await tcsApproval.Task.ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        if (timeoutCts.IsCancellationRequested)
                        {
                            var timeoutResp = new AuthResponse
                            {
                                Success = false,
                                Message = "Connection request timed out (no response from remote user)."
                            };
                            await _writer.WriteFrameAsync(networkStream, MessageType.AuthResponse, timeoutResp.Serialize(), ct).ConfigureAwait(false);
                        }
                        client.Close();
                        return;
                    }

                    if (accepted)
                    {
                        authenticated = true;
                    }
                    else
                    {
                        var declineResp = new AuthResponse
                        {
                            Success = false,
                            Message = "Connection was declined by the remote user."
                        };
                        await _writer.WriteFrameAsync(networkStream, MessageType.AuthResponse, declineResp.Serialize(), ct).ConfigureAwait(false);
                        client.Close();
                        StatusChanged?.Invoke($"Declined incoming connection from {clientMachineName} ({clientIp})");
                        return;
                    }
                }
                finally
                {
                    IncomingConnectionDismissed?.Invoke();
                }
            }

            if (!authenticated)
            {
                client.Close();
                return;
            }

            // PIN or Approval Verified! Reset any failed attempts
            _settingsManager?.ResetFailedAttempts(clientIp);

            // Send success response with screen geometry
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
            SystemPowerManager.AcquireKeepAwake();
            SystemPowerManager.WakeDisplay();

            // Expose session stream for outbound chat sends
            lock (_sessionStreamLock)
            {
                _sessionStream = networkStream;
            }

            // Associate input injector with new session generation
            int sessionId = Interlocked.Increment(ref _sessionGeneration);
            _inputInjector.ResetSession(sessionId);

            // Run Video Streaming and Input Receiving concurrently
            var streamTask = Task.Run(() => StreamScreenLoopAsync(networkStream, sessionCts), ct);
            var inputTask = Task.Run(() => ReceiveInputLoopAsync(networkStream, sessionCts), ct);

            await Task.WhenAny(streamTask, inputTask).ConfigureAwait(false);
            sessionCts.Cancel();

            try
            {
                await Task.WhenAll(streamTask, inputTask).ConfigureAwait(false);
            }
            catch
            {
                // Ignore task cancellation exceptions
            }
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
            // Clear session stream — wipes the outbound-chat path and all references
            lock (_sessionStreamLock)
            {
                _sessionStream = null;
            }
            ChatSessionEnded?.Invoke();

            // Reset input injector to release all pressed keys and buttons and drain queues
            _inputInjector.ResetSession(0);

            SystemPowerManager.ReleaseKeepAwake();

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
                var bitmap = _capturer.CaptureFrame();
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
            catch (Exception ex) when (ex is IOException or SocketException or EndOfStreamException)
            {
                // Network connection severed by client
                sessionCts.Cancel();
                break;
            }
            catch (Exception ex)
            {
                // Transient capture or encode glitch — do not terminate session
                Debug.WriteLine($"[AgentServer] Capture glitch: {ex.Message}");
                await Task.Delay(50, ct).ConfigureAwait(false);
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

                    case MessageType.SendCtrlAltDel:
                        DesktopManager.SendCtrlAltDel();
                        break;

                    case MessageType.PowerAction:
                        var powerMsg = PowerActionMessage.Deserialize(payload);
                        SystemPowerManager.ExecutePowerAction(powerMsg.Action);
                        break;

                    case MessageType.UnlockWithOsPassword:
                        var unlockMsg = UnlockWithOsPasswordMessage.Deserialize(payload);
                        _ = Task.Run(() => DesktopManager.UnlockWithPassword(unlockMsg.Password));
                        break;

                    case MessageType.ChatMessage:
                        try
                        {
                            var chatMsg = ChatMessagePayload.Deserialize(payload);
                            ChatMessageReceived?.Invoke(chatMsg);
                        }
                        catch { /* malformed payload — discard */ }
                        break;

                    case MessageType.ChatTypingIndicator:
                        ControllerTypingStarted?.Invoke();
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or SocketException or EndOfStreamException)
            {
                // Network connection closed
                sessionCts.Cancel();
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AgentServer] Input processing glitch: {ex.Message}");
            }
        }
    }

    /// <summary>Sends a chat message to the connected Controller over the active session stream.</summary>
    public async ValueTask SendChatMessageAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > ChatMessagePayload.MaxTextLength)
            text = text[..ChatMessagePayload.MaxTextLength];

        NetworkStream? stream;
        lock (_sessionStreamLock)
        {
            stream = _sessionStream;
        }
        if (stream == null) return;

        try
        {
            var msg = new ChatMessagePayload
            {
                SenderName = Environment.MachineName,
                TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Text = text
            };
            await _writer.WriteFrameAsync(stream, MessageType.ChatMessage, msg.Serialize()).ConfigureAwait(false);
        }
        catch { }
    }

    /// <summary>Sends a zero-payload typing indicator to the connected Controller.</summary>
    public async ValueTask SendTypingIndicatorAsync()
    {
        NetworkStream? stream;
        lock (_sessionStreamLock)
        {
            stream = _sessionStream;
        }
        if (stream == null) return;
        try
        {
            await _writer.WriteFrameAsync(stream, MessageType.ChatTypingIndicator, Array.Empty<byte>()).ConfigureAwait(false);
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        if (_unattendedKeepAwakeAcquired)
        {
            _unattendedKeepAwakeAcquired = false;
            SystemPowerManager.ReleaseKeepAwake();
        }
        _pinManager.UnattendedAccessChanged -= UpdateUnattendedPowerState;
        _discoveryResponder.Dispose();
        _capturer.Dispose();
        _inputInjector.Dispose();
    }
}

public sealed class IncomingConnectionEventArgs : EventArgs
{
    private readonly TaskCompletionSource<bool> _tcs;

    public string Endpoint { get; }
    public string ClientIp { get; }
    public string ClientMachineName { get; }

    public IncomingConnectionEventArgs(string endpoint, string clientIp, string clientMachineName, TaskCompletionSource<bool> tcs)
    {
        Endpoint = endpoint;
        ClientIp = clientIp;
        ClientMachineName = clientMachineName;
        _tcs = tcs;
    }

    public void Accept() => _tcs.TrySetResult(true);
    public void Reject() => _tcs.TrySetResult(false);
}

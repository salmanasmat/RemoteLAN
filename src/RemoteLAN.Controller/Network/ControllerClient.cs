using System.IO;
using System.Net.Sockets;
using RemoteLAN.Protocol.Messages;
using RemoteLAN.Protocol.Transport;

namespace RemoteLAN.Controller.Network;

public enum ControllerState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

public sealed class ControllerClient : IDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private CancellationTokenSource? _sessionCts;
    private readonly NetworkFrameWriter _writer = new();
    private readonly object _lock = new();

    public ControllerState State { get; private set; } = ControllerState.Disconnected;
    public int RemoteScreenWidth { get; private set; }
    public int RemoteScreenHeight { get; private set; }

    public event Action<ControllerState, string>? StateChanged;
    public event Action<byte[]>? FrameReceived;
    public event Action<int, int>? ScreenResolutionReceived;

    public async Task ConnectAsync(string host, int port, string pin, CancellationToken externalCt = default)
    {
        Disconnect();

        SetState(ControllerState.Connecting, $"Connecting to {host}:{port}...");

        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _sessionCts.Token;

        try
        {
            var client = new TcpClient();
            client.NoDelay = true;
            client.ReceiveBufferSize = 131072;
            client.SendBufferSize = 65536;

            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);

            lock (_lock)
            {
                _tcpClient = client;
                _networkStream = client.GetStream();
            }

            // Send PIN Handshake
            var authReq = new AuthRequest { Pin = pin };
            await _writer.WriteFrameAsync(_networkStream, MessageType.AuthRequest, authReq.Serialize(), ct).ConfigureAwait(false);

            // Await Auth Response
            var (respType, respLength) = await NetworkFrameReader.ReadFrameHeaderAsync(_networkStream, ct).ConfigureAwait(false);
            if (respType != MessageType.AuthResponse)
            {
                throw new InvalidDataException("Unexpected response header during handshake.");
            }

            byte[] authPayload = await NetworkFrameReader.ReadPayloadAsync(_networkStream, respLength, ct).ConfigureAwait(false);
            var authResp = AuthResponse.Deserialize(authPayload);

            if (!authResp.Success)
            {
                Disconnect();
                SetState(ControllerState.Error, $"Authentication failed: {authResp.Message}");
                return;
            }

            RemoteScreenWidth = authResp.ScreenWidth;
            RemoteScreenHeight = authResp.ScreenHeight;
            ScreenResolutionReceived?.Invoke(RemoteScreenWidth, RemoteScreenHeight);

            SetState(ControllerState.Connected, "Connected");

            // Start receiving stream in background
            _ = Task.Run(() => ReceiveLoopAsync(_networkStream, _sessionCts), ct);
        }
        catch (OperationCanceledException)
        {
            Disconnect();
            SetState(ControllerState.Disconnected, "Connection cancelled.");
        }
        catch (Exception ex)
        {
            Disconnect();
            SetState(ControllerState.Error, $"Connection error: {ex.Message}");
        }
    }

    private async Task ReceiveLoopAsync(NetworkStream stream, CancellationTokenSource sessionCts)
    {
        var ct = sessionCts.Token;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var (type, length) = await NetworkFrameReader.ReadFrameHeaderAsync(stream, ct).ConfigureAwait(false);
                byte[] payload = await NetworkFrameReader.ReadPayloadAsync(stream, length, ct).ConfigureAwait(false);

                if (type == MessageType.ScreenFrame)
                {
                    FrameReceived?.Invoke(payload);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or EndOfStreamException or OperationCanceledException)
        {
            // Disconnect occurred
        }
        catch (Exception ex)
        {
            SetState(ControllerState.Error, $"Stream error: {ex.Message}");
        }
        finally
        {
            Disconnect();
            SetState(ControllerState.Disconnected, "Disconnected from remote host.");
        }
    }

    public async ValueTask SendMouseMoveAsync(double normalizedX, double normalizedY)
    {
        if (State != ControllerState.Connected || _networkStream == null) return;

        try
        {
            var msg = new MouseMoveMessage
            {
                NormalizedX = normalizedX,
                NormalizedY = normalizedY
            };
            await _writer.WriteFrameAsync(_networkStream, MessageType.MouseMove, msg.Serialize()).ConfigureAwait(false);
        }
        catch { }
    }

    public async ValueTask SendMouseButtonAsync(MouseButtonType button, MouseButtonAction action)
    {
        if (State != ControllerState.Connected || _networkStream == null) return;

        try
        {
            var msg = new MouseButtonMessage
            {
                Button = button,
                Action = action
            };
            await _writer.WriteFrameAsync(_networkStream, MessageType.MouseButton, msg.Serialize()).ConfigureAwait(false);
        }
        catch { }
    }

    public async ValueTask SendMouseWheelAsync(int delta)
    {
        if (State != ControllerState.Connected || _networkStream == null) return;

        try
        {
            var msg = new MouseWheelMessage
            {
                Delta = delta
            };
            await _writer.WriteFrameAsync(_networkStream, MessageType.MouseWheel, msg.Serialize()).ConfigureAwait(false);
        }
        catch { }
    }

    public async ValueTask SendKeyboardKeyAsync(int virtualKeyCode, KeyAction action, bool isExtended)
    {
        if (State != ControllerState.Connected || _networkStream == null) return;

        try
        {
            var msg = new KeyboardKeyMessage
            {
                VirtualKeyCode = virtualKeyCode,
                Action = action,
                IsExtendedKey = isExtended
            };
            await _writer.WriteFrameAsync(_networkStream, MessageType.KeyboardKey, msg.Serialize()).ConfigureAwait(false);
        }
        catch { }
    }

    public void Disconnect()
    {
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;

        lock (_lock)
        {
            try
            {
                _networkStream?.Dispose();
            }
            catch { }
            _networkStream = null;

            try
            {
                _tcpClient?.Close();
            }
            catch { }
            _tcpClient = null;
        }

        if (State != ControllerState.Disconnected)
        {
            SetState(ControllerState.Disconnected, "Disconnected");
        }
    }

    private void SetState(ControllerState state, string message)
    {
        State = state;
        StateChanged?.Invoke(state, message);
    }

    public void Dispose()
    {
        Disconnect();
    }
}

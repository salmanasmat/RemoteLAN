using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RemoteLAN.Capture;
using RemoteLAN.Input;
using RemoteLAN.Security;

namespace RemoteLAN.WebBridge;

public sealed class WebBridgeServer : IWebBridgeServer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private readonly int _port;
    private readonly PinManager _pinManager;
    private readonly SettingsManager? _settingsManager;
    private readonly IScreenCapturer _capturer;
    private readonly IInputInjector _inputInjector;
    private readonly int _targetFps;
    private readonly X509Certificate2? _certificate;

    private WebApplication? _app;
    private CancellationTokenSource? _serverCts;
    private readonly object _stateLock = new();
    private bool _isRunning;
    private bool _disposed;

    // Active session tracking (Single-session policy)
    private WebSocket? _activeSessionSocket;
    private CancellationTokenSource? _activeSessionCts;
    private readonly object _sessionLock = new();

    public int Port => _port;
    public bool IsRunning => _isRunning;
    public string? CertificateThumbprint => _certificate?.Thumbprint;

    public event Action<string>? StatusChanged;
    public event Action<string>? ClientConnected;
    public event Action? ClientDisconnected;

    public WebBridgeServer(
        int port = 8443,
        PinManager? pinManager = null,
        SettingsManager? settingsManager = null,
        IScreenCapturer? capturer = null,
        IInputInjector? inputInjector = null,
        int targetFps = 15,
        bool useTls = true)
    {
        _port = port;
        _pinManager = pinManager ?? new PinManager();
        _settingsManager = settingsManager;
        _capturer = capturer ?? new ScreenCapturer();
        _inputInjector = inputInjector ?? new InputInjector();
        _targetFps = targetFps;

        if (useTls)
        {
            try
            {
                _certificate = WebBridgeCertificateHelper.GetOrCreateCertificate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebBridgeServer] Could not initialize TLS certificate: {ex.Message}");
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isRunning || _disposed) return;
            _serverCts = new CancellationTokenSource();
        }

        try
        {
            FirewallHelper.EnsureWebBridgePortOpen(_port);

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Any, _port, listenOptions =>
                {
                    listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;

                    if (_certificate != null)
                    {
                        listenOptions.UseHttps(_certificate);
                    }
                });
            });

            var app = builder.Build();

            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(15)
            });

            // Map static routes
            app.MapGet("/", async context => await ServeClientHtmlAsync(context));
            app.MapGet("/index.html", async context => await ServeClientHtmlAsync(context));
            app.MapGet("/health", async context =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    service = "RemoteLAN WebBridge",
                    port = _port,
                    tls = _certificate != null,
                    fps = _targetFps
                }));
            });

            // Certificate download for iOS trust setup
            app.MapGet("/cert", async context =>
            {
                if (_certificate != null)
                {
                    context.Response.ContentType = "application/x-x509-ca-cert";
                    context.Response.Headers.Append("Content-Disposition", "attachment; filename=\"remotelan-webbridge.crt\"");
                    byte[] certBytes = _certificate.Export(X509ContentType.Cert);
                    await context.Response.Body.WriteAsync(certBytes);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("No certificate configured.");
                }
            });

            // WebSocket bridge endpoint
            app.Map("/ws", async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("WebSocket connection expected.");
                    return;
                }

                string remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await HandleWebSocketSessionAsync(socket, remoteIp, _serverCts?.Token ?? CancellationToken.None);
            });

            _app = app;
            await _app.StartAsync(_serverCts.Token);

            lock (_stateLock)
            {
                _isRunning = true;
            }

            StatusChanged?.Invoke($"Listening on {(_certificate != null ? "https" : "http")}://0.0.0.0:{_port}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebBridgeServer] StartAsync failed: {ex.Message}");
            StatusChanged?.Invoke($"Failed to start: {ex.Message}");
            throw;
        }
    }

    public async Task StopAsync()
    {
        lock (_stateLock)
        {
            if (!_isRunning) return;
            _isRunning = false;
        }

        try
        {
            _serverCts?.Cancel();

            // Terminate active session
            lock (_sessionLock)
            {
                _activeSessionCts?.Cancel();
                _activeSessionSocket?.Dispose();
                _activeSessionSocket = null;
                _activeSessionCts = null;
            }

            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
            }

            StatusChanged?.Invoke("Stopped");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebBridgeServer] StopAsync error: {ex.Message}");
        }
    }

    public void DisconnectSession()
    {
        lock (_sessionLock)
        {
            try
            {
                _activeSessionCts?.Cancel();
            }
            catch { }
        }
    }

    private async Task ServeClientHtmlAsync(HttpContext context)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        byte[]? htmlBytes = GetEmbeddedClientHtmlBytes();

        if (htmlBytes != null && htmlBytes.Length > 0)
        {
            await context.Response.Body.WriteAsync(htmlBytes);
        }
        else
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("RemoteLAN WebBridge client HTML resource not found.");
        }
    }

    private static byte[]? GetEmbeddedClientHtmlBytes()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("RemoteLAN.WebBridge.Resources.index.html");
        if (stream != null)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        // Fallback to checking disk in development environment
        string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebBridge", "Resources", "index.html");
        if (File.Exists(localPath))
        {
            return File.ReadAllBytes(localPath);
        }

        return null;
    }

    private static string? ExtractHostHeader(string requestText)
    {
        int idx = requestText.IndexOf("Host:", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            int start = idx + 5;
            int end = requestText.IndexOfAny(new[] { '\r', '\n' }, start);
            if (end > start)
            {
                return requestText.Substring(start, end - start).Trim();
            }
        }
        return null;
    }

    private async Task HandleWebSocketSessionAsync(WebSocket socket, string clientIp, CancellationToken serverToken)
    {
        // 1. Check IP lockout first
        if (_settingsManager != null && _settingsManager.IsIpLockedOut(clientIp, out var remaining))
        {
            string lockedOutJson = JsonSerializer.Serialize(new
            {
                type = "auth_failed",
                reason = $"Too many failed attempts. Locked out for {(int)remaining.TotalMinutes}m {remaining.Seconds}s.",
                lockedOut = true
            });
            await SendTextMessageAsync(socket, lockedOutJson, serverToken);
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "IP locked out", serverToken);
            return;
        }

        var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
        var inputHandler = new WebBridgeInputHandler(_inputInjector);
        var streamer = new WebBridgeStreamer(_capturer, jpegQuality: 65, targetFps: _targetFps);
        bool isAuthenticated = false;
        Task? streamingTask = null;

        var buffer = new byte[8192];

        try
        {
            while (!sessionCts.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var receiveResult = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), sessionCts.Token);
                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (receiveResult.MessageType == WebSocketMessageType.Text)
                {
                    string json = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("type", out var typeProp)) continue;
                    string? type = typeProp.GetString();

                    if (!isAuthenticated)
                    {
                        if (type == "auth")
                        {
                            string pin = root.TryGetProperty("pin", out var pProp)
                                ? pProp.GetString() ?? ""
                                : (root.TryGetProperty("password", out var pwdProp) ? pwdProp.GetString() ?? "" : "");
                            if (_pinManager.ValidatePin(pin))
                            {
                                isAuthenticated = true;
                                _settingsManager?.ResetFailedAttempts(clientIp);

                                // Single session policy: close any prior active session
                                lock (_sessionLock)
                                {
                                    if (_activeSessionSocket != null && _activeSessionSocket != socket)
                                    {
                                        try
                                        {
                                            _activeSessionCts?.Cancel();
                                        }
                                        catch { }
                                    }
                                    _activeSessionSocket = socket;
                                    _activeSessionCts = sessionCts;
                                }

                                ClientConnected?.Invoke(clientIp);

                                double initCurX = 0.5;
                                double initCurY = 0.5;
                                try
                                {
                                    if (GetCursorPos(out var pt))
                                    {
                                        initCurX = Math.Clamp((double)pt.X / Math.Max(1, _capturer.Width), 0.0, 1.0);
                                        initCurY = Math.Clamp((double)pt.Y / Math.Max(1, _capturer.Height), 0.0, 1.0);
                                    }
                                }
                                catch { }

                                string authOk = JsonSerializer.Serialize(new
                                {
                                    type = "auth_ok",
                                    screenWidth = _capturer.Width,
                                    screenHeight = _capturer.Height,
                                    cursorX = initCurX,
                                    cursorY = initCurY
                                });
                                await SendTextMessageAsync(socket, authOk, sessionCts.Token);

                                // Start streaming desktop frames
                                streamingTask = Task.Run(() => streamer.StreamLoopAsync(socket, sessionCts.Token));
                            }
                            else
                            {
                                TimeSpan rem = TimeSpan.Zero;
                                bool isLocked = _settingsManager != null && _settingsManager.IsIpLockedOut(clientIp, out rem);

                                string authFail = JsonSerializer.Serialize(new
                                {
                                    type = "auth_failed",
                                    reason = isLocked
                                        ? $"Too many failed attempts. Locked out for {(int)rem.TotalMinutes}m {rem.Seconds}s."
                                        : "Invalid Access PIN or Password.",
                                    lockedOut = isLocked
                                });
                                await SendTextMessageAsync(socket, authFail, sessionCts.Token);

                                if (isLocked)
                                {
                                    await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "IP locked out", sessionCts.Token);
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Authenticated session commands
                        if (type == "ping")
                        {
                            await SendTextMessageAsync(socket, "{\"type\":\"pong\"}", sessionCts.Token);
                        }
                        else
                        {
                            inputHandler.HandleInputMessage(root);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebBridgeServer] Session error: {ex.Message}");
        }
        finally
        {
            sessionCts.Cancel();

            if (streamingTask != null)
            {
                try { await streamingTask; } catch { }
            }

            lock (_sessionLock)
            {
                if (_activeSessionSocket == socket)
                {
                    _activeSessionSocket = null;
                    _activeSessionCts = null;
                    ClientDisconnected?.Invoke();
                }
            }

            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended", CancellationToken.None);
                }
                catch { }
            }
        }
    }

    private static async Task SendTextMessageAsync(WebSocket socket, string message, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open) return;
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAsync().GetAwaiter().GetResult();
        _certificate?.Dispose();
    }
}

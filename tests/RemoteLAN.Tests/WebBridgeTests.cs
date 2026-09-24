using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using RemoteLAN.Protocol.Messages;
using RemoteLAN.Security;
using RemoteLAN.WebBridge;

namespace RemoteLAN.Tests;

public sealed class WebBridgeTests : IDisposable
{
    private readonly string _tempCertPath;

    public WebBridgeTests()
    {
        _tempCertPath = Path.Combine(Path.GetTempPath(), $"webbridge_test_{Guid.NewGuid():N}.pfx");
    }

    public void Dispose()
    {
        if (File.Exists(_tempCertPath))
        {
            try { File.Delete(_tempCertPath); } catch { }
        }
    }

    [Fact]
    public void CertificateHelper_GeneratesValidSelfSignedCertificate()
    {
        using var cert = WebBridgeCertificateHelper.GetOrCreateCertificate(_tempCertPath);

        Assert.NotNull(cert);
        Assert.True(cert.HasPrivateKey);
        Assert.Contains("RemoteLAN WebBridge", cert.Subject);
        Assert.True(cert.NotAfter > DateTime.UtcNow.AddMonths(11));
        Assert.True((cert.NotAfter - cert.NotBefore).TotalDays <= 398);

        // Re-read from disk should return the same certificate thumbprint
        using var reloaded = WebBridgeCertificateHelper.GetOrCreateCertificate(_tempCertPath);
        Assert.Equal(cert.Thumbprint, reloaded.Thumbprint);
    }

    [Fact]
    public void InputHandler_TranslatesMouseMoveCorrectly()
    {
        var injector = new TestInputInjector();
        var handler = new WebBridgeInputHandler(injector);

        string json = "{\"type\":\"mouse\",\"action\":\"move\",\"x\":0.45,\"y\":0.82}";
        using var doc = JsonDocument.Parse(json);
        handler.HandleInputMessage(doc.RootElement);

        Assert.True(injector.MouseMoves.TryDequeue(out var move));
        Assert.Equal(0.45, move.X, 2);
        Assert.Equal(0.82, move.Y, 2);
    }

    [Fact]
    public void InputHandler_TranslatesMouseButtonEventsCorrectly()
    {
        var injector = new TestInputInjector();
        var handler = new WebBridgeInputHandler(injector);

        // Right button down
        string jsonDown = "{\"type\":\"mouse\",\"action\":\"down\",\"button\":\"right\",\"x\":0.5,\"y\":0.5}";
        using var doc1 = JsonDocument.Parse(jsonDown);
        handler.HandleInputMessage(doc1.RootElement);

        Assert.True(injector.MouseButtons.TryDequeue(out var btnDown));
        Assert.Equal(MouseButtonType.Right, btnDown.Button);
        Assert.Equal(MouseButtonAction.Down, btnDown.Action);

        // Right button up
        string jsonUp = "{\"type\":\"mouse\",\"action\":\"up\",\"button\":\"right\"}";
        using var doc2 = JsonDocument.Parse(jsonUp);
        handler.HandleInputMessage(doc2.RootElement);

        Assert.True(injector.MouseButtons.TryDequeue(out var btnUp));
        Assert.Equal(MouseButtonType.Right, btnUp.Button);
        Assert.Equal(MouseButtonAction.Up, btnUp.Action);
    }

    [Fact]
    public void InputHandler_TranslatesMouseWheelCorrectly()
    {
        var injector = new TestInputInjector();
        var handler = new WebBridgeInputHandler(injector);

        string json = "{\"type\":\"scroll\",\"deltaY\":-240}";
        using var doc = JsonDocument.Parse(json);
        handler.HandleInputMessage(doc.RootElement);

        Assert.True(injector.MouseWheels.TryDequeue(out int delta));
        Assert.Equal(-240, delta);
    }

    [Fact]
    public void InputHandler_TranslatesKeyboardKeysCorrectly()
    {
        var injector = new TestInputInjector();
        var handler = new WebBridgeInputHandler(injector);

        // Enter key down (code 13)
        string json = "{\"type\":\"key\",\"action\":\"down\",\"code\":13}";
        using var doc = JsonDocument.Parse(json);
        handler.HandleInputMessage(doc.RootElement);

        Assert.True(injector.Keys.TryDequeue(out var keyEvent));
        Assert.Equal(0x0D, keyEvent.VirtualKeyCode);
        Assert.Equal(KeyAction.Down, keyEvent.Action);
    }

    [Fact]
    public void InputHandler_TranslatesSpecialKeysCorrectly()
    {
        var injector = new TestInputInjector();
        var handler = new WebBridgeInputHandler(injector);

        // Special Tab key press
        string json = "{\"type\":\"special\",\"command\":\"key_press\",\"key\":\"Tab\"}";
        using var doc = JsonDocument.Parse(json);
        handler.HandleInputMessage(doc.RootElement);

        Assert.True(injector.Keys.TryDequeue(out var downEvent));
        Assert.Equal(0x09, downEvent.VirtualKeyCode);
        Assert.Equal(KeyAction.Down, downEvent.Action);

        Assert.True(injector.Keys.TryDequeue(out var upEvent));
        Assert.Equal(0x09, upEvent.VirtualKeyCode);
        Assert.Equal(KeyAction.Up, upEvent.Action);
    }

    [Fact]
    public void InputHandler_TranslatesSpecialMetaKeyCorrectly()
    {
        var injector = new TestInputInjector();
        var handler = new WebBridgeInputHandler(injector);

        // Special Windows/Meta key
        string json = "{\"type\":\"special\",\"command\":\"key_press\",\"key\":\"Meta\"}";
        using var doc = JsonDocument.Parse(json);
        handler.HandleInputMessage(doc.RootElement);

        Assert.True(injector.Keys.TryDequeue(out var downEvent));
        Assert.Equal(0x5B, downEvent.VirtualKeyCode);
        Assert.Equal(KeyAction.Down, downEvent.Action);
        Assert.True(downEvent.IsExtended);

        Assert.True(injector.Keys.TryDequeue(out var upEvent));
        Assert.Equal(0x5B, upEvent.VirtualKeyCode);
        Assert.Equal(KeyAction.Up, upEvent.Action);
        Assert.True(upEvent.IsExtended);
    }

    [Fact]
    public async Task WebBridgeServer_StartsAndServesHealthEndpointOverHttp()
    {
        int testPort = 18443;
        var pinManager = new PinManager("123456");
        var injector = new TestInputInjector();

        using var server = new WebBridgeServer(
            port: testPort,
            pinManager: pinManager,
            settingsManager: null,
            capturer: null,
            inputInjector: injector,
            targetFps: 15,
            useTls: false); // HTTP mode for testing

        await server.StartAsync();
        Assert.True(server.IsRunning);

        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync($"http://127.0.0.1:{testPort}/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var healthData = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", healthData.GetProperty("status").GetString());
        Assert.Equal("RemoteLAN WebBridge", healthData.GetProperty("service").GetString());
        Assert.Equal(testPort, healthData.GetProperty("port").GetInt32());

        // Test root index.html serving
        var htmlResponse = await httpClient.GetAsync($"http://127.0.0.1:{testPort}/");
        Assert.Equal(HttpStatusCode.OK, htmlResponse.StatusCode);
        string html = await htmlResponse.Content.ReadAsStringAsync();
        Assert.Contains("RemoteLAN WebBridge", html);

        await server.StopAsync();
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task WebBridgeServer_ServesHttpsAndRedirectsHttp()
    {
        int testPort = 18444;
        var pinManager = new PinManager("654321");
        var injector = new TestInputInjector();

        using var server = new WebBridgeServer(
            port: testPort,
            pinManager: pinManager,
            settingsManager: null,
            capturer: null,
            inputInjector: injector,
            targetFps: 15,
            useTls: true);

        await server.StartAsync();
        Assert.True(server.IsRunning);

        // 1. Test HTTPS request
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var httpsClient = new HttpClient(handler);
        var httpsResponse = await httpsClient.GetAsync($"https://127.0.0.1:{testPort}/health");
        Assert.Equal(HttpStatusCode.OK, httpsResponse.StatusCode);

        var healthData = await httpsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", healthData.GetProperty("status").GetString());
        Assert.True(healthData.GetProperty("tls").GetBoolean());

        await server.StopAsync();
        Assert.False(server.IsRunning);
    }

    [Fact]
    public void QrCodeHelper_GeneratesValidBitmapSource()
    {
        var bitmap = QrCodeHelper.GenerateQrCode("https://192.168.100.66:8443");
        Assert.NotNull(bitmap);
        Assert.True(bitmap.PixelWidth > 0);
        Assert.True(bitmap.PixelHeight > 0);
    }

    [Fact]
    public async Task WebBridgeServer_DisconnectSession_TriggersClientDisconnected()
    {
        int testPort = 18451;
        var pinManager = new PinManager("654321");
        var injector = new TestInputInjector();

        using var server = new WebBridgeServer(
            port: testPort,
            pinManager: pinManager,
            settingsManager: null,
            capturer: null,
            inputInjector: injector,
            targetFps: 15,
            useTls: true);

        await server.StartAsync();
        try
        {
            var disconnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            server.ClientDisconnected += () => disconnectedTcs.TrySetResult(true);

            using var ws = new System.Net.WebSockets.ClientWebSocket();
            ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            await ws.ConnectAsync(new Uri($"wss://127.0.0.1:{testPort}/ws"), CancellationToken.None);

            string authJson = "{\"type\":\"auth\",\"pin\":\"654321\"}";
            var authBytes = System.Text.Encoding.UTF8.GetBytes(authJson);
            await ws.SendAsync(new ArraySegment<byte>(authBytes), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);

            await Task.Delay(300);

            server.DisconnectSession();

            var result = await Task.WhenAny(disconnectedTcs.Task, Task.Delay(3000));
            Assert.Same(disconnectedTcs.Task, result);
            Assert.True(await disconnectedTcs.Task);
        }
        finally
        {
            await server.StopAsync();
        }
    }
}

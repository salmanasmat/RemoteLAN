using System.Diagnostics;
using System.Drawing;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using RemoteLAN.Capture;

namespace RemoteLAN.WebBridge;

public sealed class WebBridgeStreamer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private readonly IScreenCapturer _capturer;
    private readonly JpegFrameEncoder _encoder;
    private readonly int _targetFps;

    public WebBridgeStreamer(IScreenCapturer capturer, int jpegQuality = 65, int targetFps = 15)
    {
        _capturer = capturer ?? throw new ArgumentNullException(nameof(capturer));
        _encoder = new JpegFrameEncoder(jpegQuality);
        _targetFps = Math.Clamp(targetFps, 1, 60);
    }

    public async Task StreamLoopAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        int frameDelayMs = 1000 / _targetFps;
        var stopwatch = new Stopwatch();
        double lastSentX = -1;
        double lastSentY = -1;

        while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            stopwatch.Restart();

            try
            {
                Bitmap? frame = _capturer.CaptureFrame();
                if (frame != null)
                {
                    byte[] jpegBytes = _encoder.Encode(frame);

                    if (webSocket.State == WebSocketState.Open)
                    {
                        await webSocket.SendAsync(
                            new ArraySegment<byte>(jpegBytes),
                            WebSocketMessageType.Binary,
                            true,
                            cancellationToken);
                    }
                }

                // Check and broadcast cursor position if changed on the host PC
                try
                {
                    if (GetCursorPos(out var pt))
                    {
                        double curX = Math.Clamp((double)pt.X / Math.Max(1, _capturer.Width), 0.0, 1.0);
                        double curY = Math.Clamp((double)pt.Y / Math.Max(1, _capturer.Height), 0.0, 1.0);
                        if (Math.Abs(curX - lastSentX) > 0.003 || Math.Abs(curY - lastSentY) > 0.003)
                        {
                            lastSentX = curX;
                            lastSentY = curY;
                            string cursorJson = $"{{\"type\":\"cursor\",\"x\":{curX:F4},\"y\":{curY:F4}}}";
                            byte[] cursorBytes = Encoding.UTF8.GetBytes(cursorJson);
                            if (webSocket.State == WebSocketState.Open)
                            {
                                await webSocket.SendAsync(
                                    new ArraySegment<byte>(cursorBytes),
                                    WebSocketMessageType.Text,
                                    true,
                                    cancellationToken);
                            }
                        }
                    }
                }
                catch { }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebBridgeStreamer] Frame capture/stream error: {ex.Message}");
            }

            stopwatch.Stop();
            int remainingDelay = frameDelayMs - (int)stopwatch.ElapsedMilliseconds;
            if (remainingDelay > 0)
            {
                try
                {
                    await Task.Delay(remainingDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}

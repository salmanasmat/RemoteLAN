using System.IO;
using System.Windows.Media.Imaging;

namespace RemoteLAN.Rendering;

public sealed class FrameRenderer
{
    public event Action<BitmapSource>? FrameReady;
    public event Action<double>? FpsUpdated;

    private int _frameCount;
    private readonly System.Diagnostics.Stopwatch _fpsStopwatch = System.Diagnostics.Stopwatch.StartNew();

    public void ProcessJpegFrame(byte[] jpegBytes)
    {
        if (jpegBytes == null || jpegBytes.Length == 0) return;

        try
        {
            using var ms = new MemoryStream(jpegBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze(); // Crucial for cross-thread access and WPF rendering performance

            _frameCount++;
            if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
            {
                double currentFps = _frameCount * 1000.0 / _fpsStopwatch.ElapsedMilliseconds;
                FpsUpdated?.Invoke(Math.Round(currentFps, 1));
                _frameCount = 0;
                _fpsStopwatch.Restart();
            }

            FrameReady?.Invoke(bitmap);
        }
        catch
        {
            // Drop corrupt frame defensively without throwing
        }
    }

    public void Reset()
    {
        _frameCount = 0;
        _fpsStopwatch.Restart();
        FpsUpdated?.Invoke(0.0);
    }
}

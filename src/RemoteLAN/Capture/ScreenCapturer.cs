using System.Drawing;

namespace RemoteLAN.Capture;

public sealed class ScreenCapturer : IScreenCapturer
{
    private IScreenCapturer _activeCapturer;
    private int _consecutiveNullFrames;

    public int Width => _activeCapturer.Width;
    public int Height => _activeCapturer.Height;
    public string EngineName => _activeCapturer.EngineName;

    public ScreenCapturer()
    {
        var dxgi = new DxgiScreenCapturer();
        if (dxgi.Initialize() || dxgi.IsD3D11Supported)
        {
            // DXGI is supported by the system GPU (even if output duplication is temporarily unavailable due to lock)
            _activeCapturer = dxgi;
        }
        else
        {
            dxgi.Dispose();
            var gdi = new GdiScreenCapturer();
            gdi.Initialize();
            _activeCapturer = gdi;
        }
    }

    public bool Initialize()
    {
        return _activeCapturer.Initialize();
    }

    public Bitmap? CaptureFrame()
    {
        var frame = _activeCapturer.CaptureFrame();
        if (frame == null)
        {
            _consecutiveNullFrames++;
            if (_consecutiveNullFrames >= 10 && _activeCapturer is DxgiScreenCapturer)
            {
                // Fallback to GDI only after persistent unhandled failures
                _activeCapturer.Dispose();
                var gdi = new GdiScreenCapturer();
                gdi.Initialize();
                _activeCapturer = gdi;
                return _activeCapturer.CaptureFrame();
            }
        }
        else
        {
            _consecutiveNullFrames = 0;
        }

        return frame;
    }

    public void Dispose()
    {
        _activeCapturer.Dispose();
    }
}

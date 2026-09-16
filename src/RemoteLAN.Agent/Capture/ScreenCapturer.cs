using System.Drawing;

namespace RemoteLAN.Agent.Capture;

public sealed class ScreenCapturer : IScreenCapturer
{
    private IScreenCapturer _activeCapturer;

    public int Width => _activeCapturer.Width;
    public int Height => _activeCapturer.Height;
    public string EngineName => _activeCapturer.EngineName;

    public ScreenCapturer()
    {
        IScreenCapturer dxgi = new DxgiScreenCapturer();
        if (dxgi.Initialize())
        {
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
        if (frame == null && _activeCapturer is DxgiScreenCapturer)
        {
            // Fallback to GDI if DXGI encounters fatal error
            _activeCapturer.Dispose();
            var gdi = new GdiScreenCapturer();
            gdi.Initialize();
            _activeCapturer = gdi;
            return _activeCapturer.CaptureFrame();
        }
        return frame;
    }

    public void Dispose()
    {
        _activeCapturer.Dispose();
    }
}

using System.Drawing;

namespace RemoteLAN.Capture;

public sealed class ScreenCapturer : IScreenCapturer
{
    private readonly DxgiScreenCapturer? _dxgi;
    private readonly GdiScreenCapturer _gdi;
    private bool _dxgiPermanentlyUnsupported;

    public int Width => (_dxgi != null && !_dxgiPermanentlyUnsupported && _dxgi.Width > 0) ? _dxgi.Width : _gdi.Width;
    public int Height => (_dxgi != null && !_dxgiPermanentlyUnsupported && _dxgi.Height > 0) ? _dxgi.Height : _gdi.Height;
    public string EngineName => (_dxgi != null && !_dxgiPermanentlyUnsupported) ? _dxgi.EngineName : _gdi.EngineName;

    public ScreenCapturer()
    {
        _gdi = new GdiScreenCapturer();
        _gdi.Initialize();

        var dxgi = new DxgiScreenCapturer();
        if (dxgi.Initialize() || dxgi.IsD3D11Supported)
        {
            _dxgi = dxgi;
        }
        else
        {
            dxgi.Dispose();
            _dxgi = null;
            _dxgiPermanentlyUnsupported = true;
        }
    }

    public bool Initialize()
    {
        if (_dxgi != null && !_dxgiPermanentlyUnsupported)
        {
            if (_dxgi.Initialize()) return true;
        }
        return _gdi.Initialize();
    }

    public Bitmap? CaptureFrame()
    {
        // 1. If DXGI is supported and available, prioritize DXGI for high-performance desktop streaming
        if (_dxgi != null && !_dxgiPermanentlyUnsupported)
        {
            var frame = _dxgi.CaptureFrame();
            if (frame != null)
            {
                return frame;
            }

            // DXGI returned null (e.g. session locked / Winlogon active).
            // Seamlessly fall back to GDI capturer attached to active input desktop
            var gdiFrame = _gdi.CaptureFrame();
            if (gdiFrame != null)
            {
                return gdiFrame;
            }
        }

        // 2. Pure GDI capture
        return _gdi.CaptureFrame();
    }

    public void Dispose()
    {
        _dxgi?.Dispose();
        _gdi.Dispose();
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using RemoteLAN.Security;

namespace RemoteLAN.Capture;

public sealed class GdiScreenCapturer : IScreenCapturer
{
    private Bitmap? _screenBitmap;
    private Graphics? _screenGraphics;
    private Bitmap? _placeholderBitmap;
    private Graphics? _placeholderGraphics;
    private int _width;
    private int _height;

    public int Width => _width > 0 ? _width : 1920;
    public int Height => _height > 0 ? _height : 1080;
    public string EngineName => "GDI BitBlt";

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private string? _lastDesktopName;

    public bool Initialize()
    {
        try
        {
            _width = GetSystemMetrics(SM_CXSCREEN);
            _height = GetSystemMetrics(SM_CYSCREEN);

            if (_width <= 0 || _height <= 0)
            {
                _width = 1920;
                _height = 1080;
            }

            _screenGraphics?.Dispose();
            _screenBitmap?.Dispose();

            _screenBitmap = new Bitmap(_width, _height, PixelFormat.Format32bppRgb);
            _screenGraphics = Graphics.FromImage(_screenBitmap);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public Bitmap? CaptureFrame()
    {
        // 1. Ensure thread is attached to the currently active input desktop (Default vs Winlogon)
        DesktopManager.EnsureThreadOnInputDesktop(out string currentDesktop);
        if (_lastDesktopName != null && !string.Equals(_lastDesktopName, currentDesktop, StringComparison.OrdinalIgnoreCase))
        {
            // Desktop switched! Invalidate screen graphics to re-bind to the new desktop
            Initialize();
        }
        _lastDesktopName = currentDesktop;

        int curWidth = GetSystemMetrics(SM_CXSCREEN);
        int curHeight = GetSystemMetrics(SM_CYSCREEN);
        if (_screenBitmap == null || _screenGraphics == null || (curWidth > 0 && curHeight > 0 && (curWidth != _width || curHeight != _height)))
        {
            if (!Initialize())
            {
                return RenderPlaceholder();
            }
        }

        try
        {
            _screenGraphics!.CopyFromScreen(0, 0, 0, 0, new Size(_width, _height), CopyPixelOperation.SourceCopy);
            return _screenBitmap;
        }
        catch
        {
            // Desktop session locked or non-interactive; try re-init once
            try
            {
                if (Initialize())
                {
                    _screenGraphics!.CopyFromScreen(0, 0, 0, 0, new Size(_width, _height), CopyPixelOperation.SourceCopy);
                    return _screenBitmap;
                }
            }
            catch { }

            return RenderPlaceholder();
        }
    }

    private Bitmap RenderPlaceholder()
    {
        int curW = _width > 0 ? _width : GetSystemMetrics(SM_CXSCREEN);
        int curH = _height > 0 ? _height : GetSystemMetrics(SM_CYSCREEN);
        string? reason = !DesktopManager.IsAdministrator
            ? "Run RemoteLAN as Administrator to view and unlock the Windows sign-in screen."
            : null;

        return PlaceholderFrameHelper.RenderLockPlaceholder(
            ref _placeholderBitmap,
            ref _placeholderGraphics,
            curW,
            curH,
            EngineName,
            reason);
    }

    public void Dispose()
    {
        try { _placeholderGraphics?.Dispose(); } catch { }
        _placeholderGraphics = null;

        try { _placeholderBitmap?.Dispose(); } catch { }
        _placeholderBitmap = null;

        try { _screenGraphics?.Dispose(); } catch { }
        _screenGraphics = null;

        try { _screenBitmap?.Dispose(); } catch { }
        _screenBitmap = null;
    }
}

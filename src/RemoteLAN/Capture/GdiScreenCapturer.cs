using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RemoteLAN.Capture;

public sealed class GdiScreenCapturer : IScreenCapturer
{
    private Bitmap? _screenBitmap;
    private Graphics? _screenGraphics;
    private int _width;
    private int _height;

    public int Width => _width;
    public int Height => _height;
    public string EngineName => "GDI BitBlt";

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

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

            _screenBitmap?.Dispose();
            _screenGraphics?.Dispose();

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
        int curWidth = GetSystemMetrics(SM_CXSCREEN);
        int curHeight = GetSystemMetrics(SM_CYSCREEN);
        if (_screenBitmap == null || _screenGraphics == null || (curWidth > 0 && curHeight > 0 && (curWidth != _width || curHeight != _height)))
        {
            if (!Initialize()) return null;
        }

        try
        {
            _screenGraphics!.CopyFromScreen(0, 0, 0, 0, new Size(_width, _height), CopyPixelOperation.SourceCopy);
            return _screenBitmap;
        }
        catch (Exception ex)
        {
            // If running in headless session, locked screen, or secure desktop, render a graceful placeholder frame
            return GeneratePlaceholderFrame(ex.Message);
        }
    }

    private Bitmap? GeneratePlaceholderFrame(string reason)
    {
        try
        {
            if (_screenBitmap == null || _screenGraphics == null)
            {
                if (!Initialize()) return null;
            }

            using var brush = new SolidBrush(Color.FromArgb(24, 24, 27));
            _screenGraphics!.FillRectangle(brush, 0, 0, _width, _height);

            using var textBrush = new SolidBrush(Color.FromArgb(228, 228, 231));
            using var font = new Font(FontFamily.GenericSansSerif, 20, FontStyle.Bold);
            using var subFont = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Regular);

            _screenGraphics.DrawString("RemoteLAN — Screen Unavailable", font, textBrush, new PointF(60, 60));
            _screenGraphics.DrawString($"Status: {reason} | Desktop session locked or non-interactive.", subFont, textBrush, new PointF(60, 110));
            _screenGraphics.DrawString($"Resolution: {_width}x{_height} | Time: {DateTime.Now:HH:mm:ss}", subFont, textBrush, new PointF(60, 140));

            return _screenBitmap;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        try
        {
            _screenGraphics?.Dispose();
        }
        catch { }
        _screenGraphics = null;

        try
        {
            _screenBitmap?.Dispose();
        }
        catch { }
        _screenBitmap = null;
    }
}

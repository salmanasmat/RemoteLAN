using System.Drawing;

namespace RemoteLAN.Capture;

public interface IScreenCapturer : IDisposable
{
    int Width { get; }
    int Height { get; }
    string EngineName { get; }
    bool Initialize();
    /// <summary>
    /// Captures the current display frame. The returned Bitmap is an internally cached/reused buffer
    /// owned by the capturer and must NOT be disposed by the caller.
    /// </summary>
    Bitmap? CaptureFrame();
}

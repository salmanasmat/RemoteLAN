using System.Drawing;

namespace RemoteLAN.Agent.Capture;

public interface IScreenCapturer : IDisposable
{
    int Width { get; }
    int Height { get; }
    string EngineName { get; }
    bool Initialize();
    Bitmap? CaptureFrame();
}

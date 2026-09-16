using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RemoteLAN.Agent.Capture;

public sealed class DxgiScreenCapturer : IScreenCapturer
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _stagingTexture;
    private Bitmap? _outputBitmap;
    private int _width;
    private int _height;

    public int Width => _width;
    public int Height => _height;
    public string EngineName => "DXGI Desktop Duplication";

    public bool Initialize()
    {
        Dispose();

        try
        {
            var creationFlags = DeviceCreationFlags.BgraSupport;
            var featureLevels = new[]
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0
            };

            var d3dResult = D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                creationFlags,
                featureLevels,
                out _device,
                out _context);

            if (!d3dResult.Success || _device == null || _context == null)
            {
                return false;
            }

            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            if (dxgiDevice == null) return false;

            if (dxgiDevice.GetAdapter(out IDXGIAdapter? adapter).Failure || adapter == null)
                return false;

            using (adapter)
            {
                if (adapter.EnumOutputs(0, out IDXGIOutput? output).Failure || output == null)
                    return false;

                using (output)
                {
                    using var output1 = output.QueryInterface<IDXGIOutput1>();
                    if (output1 == null) return false;

                    _duplication = output1.DuplicateOutput(_device);
                    if (_duplication == null) return false;

                    var desc = _duplication.Description;
                    _width = (int)desc.ModeDescription.Width;
                    _height = (int)desc.ModeDescription.Height;

                    var stagingDesc = new Texture2DDescription
                    {
                        Width = (uint)_width,
                        Height = (uint)_height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CPUAccessFlags = CpuAccessFlags.Read,
                        MiscFlags = ResourceOptionFlags.None
                    };

                    _stagingTexture = _device.CreateTexture2D(stagingDesc);
                    _outputBitmap = new Bitmap(_width, _height, PixelFormat.Format32bppRgb);

                    return true;
                }
            }
        }
        catch
        {
            Dispose();
            return false;
        }
    }

    public unsafe Bitmap? CaptureFrame()
    {
        if (_duplication == null || _device == null || _context == null || _stagingTexture == null || _outputBitmap == null)
        {
            if (!Initialize()) return null;
        }

        try
        {
            var acquireResult = _duplication!.AcquireNextFrame(
                50,
                out _,
                out IDXGIResource? desktopResource);

            if (acquireResult.Failure)
            {
                if (acquireResult.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code)
                {
                    // No new frame available; return current cached frame
                    return _outputBitmap;
                }

                // Access lost or display mode change: re-initialize
                if (Initialize())
                {
                    return _outputBitmap;
                }
                return null;
            }

            using (desktopResource)
            {
                if (desktopResource != null)
                {
                    using var desktopTexture = desktopResource.QueryInterface<ID3D11Texture2D>();
                    if (desktopTexture != null)
                    {
                        _context!.CopyResource(_stagingTexture!, desktopTexture);
                    }
                }
            }

            _duplication.ReleaseFrame();

            // Map staging texture to copy bytes into the Bitmap
            var mapped = _context!.Map(_stagingTexture!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var bmpData = _outputBitmap!.LockBits(
                    new Rectangle(0, 0, _width, _height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppRgb);

                try
                {
                    byte* srcPtr = (byte*)mapped.DataPointer;
                    byte* dstPtr = (byte*)bmpData.Scan0;
                    int srcRowPitch = (int)mapped.RowPitch;
                    int dstRowPitch = bmpData.Stride;
                    int copyRowLength = Math.Min(srcRowPitch, dstRowPitch);

                    for (int y = 0; y < _height; y++)
                    {
                        Buffer.MemoryCopy(srcPtr + (y * srcRowPitch), dstPtr + (y * dstRowPitch), dstRowPitch, copyRowLength);
                    }
                }
                finally
                {
                    _outputBitmap.UnlockBits(bmpData);
                }
            }
            finally
            {
                _context.Unmap(_stagingTexture!, 0);
            }

            return _outputBitmap;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _outputBitmap?.Dispose();
        _outputBitmap = null;

        _stagingTexture?.Dispose();
        _stagingTexture = null;

        _duplication?.Dispose();
        _duplication = null;

        _context?.Dispose();
        _context = null;

        _device?.Dispose();
        _device = null;
    }
}

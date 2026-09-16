using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace RemoteLAN.Capture;

public sealed class JpegFrameEncoder
{
    private readonly ImageCodecInfo _jpegEncoder;
    private readonly EncoderParameters _encoderParameters;

    public int Quality { get; }

    public JpegFrameEncoder(int quality = 70)
    {
        Quality = Math.Clamp(quality, 10, 100);
        _jpegEncoder = GetEncoder(ImageFormat.Jpeg);
        _encoderParameters = new EncoderParameters(1)
        {
            Param = { [0] = new EncoderParameter(Encoder.Quality, (long)Quality) }
        };
    }

    public byte[] Encode(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, _jpegEncoder, _encoderParameters);
        return ms.ToArray();
    }

    public void EncodeToStream(Bitmap bitmap, Stream destination)
    {
        bitmap.Save(destination, _jpegEncoder, _encoderParameters);
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageDecoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        throw new InvalidOperationException("JPEG encoder not found in system codecs.");
    }
}

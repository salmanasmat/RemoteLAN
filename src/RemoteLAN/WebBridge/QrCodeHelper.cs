using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace RemoteLAN.WebBridge;

public static class QrCodeHelper
{
    public static BitmapSource GenerateQrCode(string payload, int pixelsPerModule = 8)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        byte[] qrCodeBytes = qrCode.GetGraphic(pixelsPerModule);

        using var ms = new MemoryStream(qrCodeBytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

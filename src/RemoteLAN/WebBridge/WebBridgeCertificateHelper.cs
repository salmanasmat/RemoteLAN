using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RemoteLAN.WebBridge;

public static class WebBridgeCertificateHelper
{
    private const string InternalCertPassword = "RemoteLAN_WebBridge_Key";

    public static string GetDefaultCertificatePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RemoteLAN");
        return Path.Combine(dir, "webbridge_cert.pfx");
    }

    public static X509Certificate2 GetOrCreateCertificate(string? customPath = null)
    {
        string certPath = customPath ?? GetDefaultCertificatePath();

        if (File.Exists(certPath))
        {
            try
            {
                var loaded = new X509Certificate2(certPath, InternalCertPassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                // Verify cert has private key, is valid for at least 30 more days, and conforms to Apple's <= 398 days lifetime rule
                double totalDays = (loaded.NotAfter - loaded.NotBefore).TotalDays;
                if (loaded.HasPrivateKey && loaded.NotAfter > DateTime.UtcNow.AddDays(30) && totalDays <= 398)
                {
                    return loaded;
                }
                Debug.WriteLine($"[WebBridge] Certificate invalid or violates Apple <= 398 days validity policy ({totalDays:F0} days). Regenerating.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebBridge] Existing certificate invalid or unreadable: {ex.Message}. Regenerating.");
            }
        }

        var cert = GenerateSelfSignedCertificate();
        try
        {
            string? dir = Path.GetDirectoryName(certPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            byte[] pfxBytes = cert.Export(X509ContentType.Pfx, InternalCertPassword);
            File.WriteAllBytes(certPath, pfxBytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebBridge] Could not save certificate to disk: {ex.Message}");
        }

        return cert;
    }

    public static X509Certificate2 GenerateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=RemoteLAN WebBridge, O=RemoteLAN, OU=Local Access",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var ipProps = ni.GetIPProperties();
                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        sanBuilder.AddIpAddress(unicast.Address);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebBridge] Error enumerating network interfaces for SAN: {ex.Message}");
        }

        request.CertificateExtensions.Add(sanBuilder.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Authentication
            false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(365); // Apple iOS 13+ & macOS 10.15+ TLS policy: validity must be <= 398 days

        using var generated = request.CreateSelfSigned(notBefore, notAfter);
        byte[] pfx = generated.Export(X509ContentType.Pfx, InternalCertPassword);
        return new X509Certificate2(pfx, InternalCertPassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }
}

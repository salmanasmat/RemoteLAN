using System.IO;
using System.Text;

namespace RemoteLAN.Protocol.Messages;

public sealed class AuthRequest
{
    public string Pin { get; set; } = string.Empty;

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write(Pin ?? string.Empty);
        writer.Flush();
        return ms.ToArray();
    }

    public static AuthRequest Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        return new AuthRequest
        {
            Pin = reader.ReadString()
        };
    }
}

public sealed class AuthResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write(Success);
        writer.Write(Message ?? string.Empty);
        writer.Write(ScreenWidth);
        writer.Write(ScreenHeight);
        writer.Flush();
        return ms.ToArray();
    }

    public static AuthResponse Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        return new AuthResponse
        {
            Success = reader.ReadBoolean(),
            Message = reader.ReadString(),
            ScreenWidth = reader.ReadInt32(),
            ScreenHeight = reader.ReadInt32()
        };
    }
}

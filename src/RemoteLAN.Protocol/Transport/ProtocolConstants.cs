namespace RemoteLAN.Protocol.Transport;

public static class ProtocolConstants
{
    public const int DefaultPort = 9191;
    public const int HeaderLength = 5; // 1 byte type + 4 bytes int32 length
    public const int MaxPayloadSize = 20 * 1024 * 1024; // 20MB safety limit
}

using System.IO;
using System.Text;

namespace RemoteLAN.Protocol.Messages;

/// <summary>
/// Represents a single chat message exchanged between Controller and Agent
/// during an active session. Intentionally ephemeral — never written to disk.
/// </summary>
public sealed class ChatMessagePayload
{
    /// <summary>The display name of the sender (machine name).</summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>UTC timestamp in milliseconds since Unix epoch.</summary>
    public long TimestampUtcMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>The message body text. Max 4096 characters enforced on send.</summary>
    public string Text { get; set; } = string.Empty;

    public const int MaxTextLength = 4096;

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write(SenderName ?? string.Empty);
        writer.Write(TimestampUtcMs);
        writer.Write(Text ?? string.Empty);
        writer.Flush();
        return ms.ToArray();
    }

    public static ChatMessagePayload Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var msg = new ChatMessagePayload
        {
            SenderName = reader.ReadString(),
            TimestampUtcMs = reader.ReadInt64(),
            Text = reader.ReadString()
        };
        if (msg.Text.Length > MaxTextLength)
        {
            throw new InvalidDataException($"Chat message text exceeds maximum allowed length of {MaxTextLength} characters.");
        }
        return msg;
    }
}

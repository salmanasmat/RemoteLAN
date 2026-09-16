using System.Buffers.Binary;
using System.IO;
using RemoteLAN.Protocol.Messages;

namespace RemoteLAN.Protocol.Transport;

public static class NetworkFrameReader
{
    /// <summary>
    /// Reads the next frame header (1 byte type + 4 bytes length).
    /// Returns (MessageType, payloadLength).
    /// Throws EndOfStreamException if the socket stream closes cleanly.
    /// </summary>
    public static async ValueTask<(MessageType Type, int Length)> ReadFrameHeaderAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] headerBuffer = new byte[ProtocolConstants.HeaderLength];
        await ReadExactAsync(stream, headerBuffer, cancellationToken).ConfigureAwait(false);

        MessageType type = (MessageType)headerBuffer[0];
        int length = BinaryPrimitives.ReadInt32BigEndian(headerBuffer.AsSpan(1, 4));

        if (length < 0 || length > ProtocolConstants.MaxPayloadSize)
        {
            throw new InvalidDataException($"Invalid frame length received: {length} bytes (max allowed: {ProtocolConstants.MaxPayloadSize}).");
        }

        return (type, length);
    }

    /// <summary>
    /// Reads exactly buffer.Length bytes from the stream.
    /// Throws EndOfStreamException if the connection is closed before full buffer is read.
    /// </summary>
    public static async ValueTask ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.Slice(totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException($"Connection closed prematurely after reading {totalRead} of {buffer.Length} bytes.");
            }
            totalRead += read;
        }
    }

    /// <summary>
    /// Reads a full payload of the specified length.
    /// </summary>
    public static async ValueTask<byte[]> ReadPayloadAsync(Stream stream, int length, CancellationToken cancellationToken = default)
    {
        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        byte[] buffer = new byte[length];
        await ReadExactAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }
}

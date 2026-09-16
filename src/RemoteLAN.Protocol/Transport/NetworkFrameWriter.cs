using System.Buffers.Binary;
using System.IO;
using RemoteLAN.Protocol.Messages;

namespace RemoteLAN.Protocol.Transport;

public sealed class NetworkFrameWriter
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async ValueTask WriteFrameAsync(Stream stream, MessageType type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[] header = new byte[ProtocolConstants.HeaderLength];
            header[0] = (byte)type;
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1, 4), payload.Length);

            // If payload is reasonably small, combine into single buffer to reduce TCP syscall overhead
            if (payload.Length <= 4096)
            {
                byte[] combined = new byte[ProtocolConstants.HeaderLength + payload.Length];
                Buffer.BlockCopy(header, 0, combined, 0, ProtocolConstants.HeaderLength);
                payload.Span.CopyTo(combined.AsSpan(ProtocolConstants.HeaderLength));
                await stream.WriteAsync(combined, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            }
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}

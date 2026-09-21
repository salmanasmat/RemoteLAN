using System.IO;
using RemoteLAN.Protocol.Messages;
using RemoteLAN.Protocol.Transport;

namespace RemoteLAN.Tests;

public class ProtocolTests
{
    [Fact]
    public void AuthRequest_RoundTrip_Serialization()
    {
        var original = new AuthRequest { Pin = "849201", ClientMachineName = "DESKTOP-TEST" };
        byte[] bytes = original.Serialize();
        var deserialized = AuthRequest.Deserialize(bytes);

        Assert.Equal(original.Pin, deserialized.Pin);
        Assert.Equal(original.ClientMachineName, deserialized.ClientMachineName);
    }

    [Fact]
    public void AuthRequest_BackwardCompatibility_WhenNoMachineName()
    {
        // Simulate older client sending only PIN string
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write("719302");
        writer.Flush();
        byte[] legacyBytes = ms.ToArray();

        var deserialized = AuthRequest.Deserialize(legacyBytes);
        Assert.Equal("719302", deserialized.Pin);
        Assert.Equal(string.Empty, deserialized.ClientMachineName);
    }

    [Fact]
    public void AuthResponse_RoundTrip_Serialization()
    {
        var original = new AuthResponse
        {
            Success = true,
            Message = "Access Granted",
            ScreenWidth = 2560,
            ScreenHeight = 1440
        };

        byte[] bytes = original.Serialize();
        var deserialized = AuthResponse.Deserialize(bytes);

        Assert.True(deserialized.Success);
        Assert.Equal(original.Message, deserialized.Message);
        Assert.Equal(original.ScreenWidth, deserialized.ScreenWidth);
        Assert.Equal(original.ScreenHeight, deserialized.ScreenHeight);
    }

    [Fact]
    public void MouseMoveMessage_RoundTrip_Serialization()
    {
        var original = new MouseMoveMessage
        {
            NormalizedX = 0.4289,
            NormalizedY = 0.8912
        };

        byte[] bytes = original.Serialize();
        var deserialized = MouseMoveMessage.Deserialize(bytes);

        Assert.Equal(original.NormalizedX, deserialized.NormalizedX, 4);
        Assert.Equal(original.NormalizedY, deserialized.NormalizedY, 4);
    }

    [Fact]
    public void MouseButtonMessage_RoundTrip_Serialization()
    {
        var original = new MouseButtonMessage
        {
            Button = MouseButtonType.Right,
            Action = MouseButtonAction.Down
        };

        byte[] bytes = original.Serialize();
        var deserialized = MouseButtonMessage.Deserialize(bytes);

        Assert.Equal(original.Button, deserialized.Button);
        Assert.Equal(original.Action, deserialized.Action);
    }

    [Fact]
    public void MouseWheelMessage_RoundTrip_Serialization()
    {
        var original = new MouseWheelMessage { Delta = -120 };
        byte[] bytes = original.Serialize();
        var deserialized = MouseWheelMessage.Deserialize(bytes);

        Assert.Equal(original.Delta, deserialized.Delta);
    }

    [Fact]
    public void KeyboardKeyMessage_RoundTrip_Serialization()
    {
        var original = new KeyboardKeyMessage
        {
            VirtualKeyCode = 0x41, // 'A'
            Action = KeyAction.Down,
            IsExtendedKey = false
        };

        byte[] bytes = original.Serialize();
        var deserialized = KeyboardKeyMessage.Deserialize(bytes);

        Assert.Equal(original.VirtualKeyCode, deserialized.VirtualKeyCode);
        Assert.Equal(original.Action, deserialized.Action);
        Assert.Equal(original.IsExtendedKey, deserialized.IsExtendedKey);
    }

    [Fact]
    public async Task FrameWriter_And_FrameReader_RoundTrip()
    {
        using var stream = new MemoryStream();
        var writer = new NetworkFrameWriter();

        byte[] samplePayload = new byte[1024];
        for (int i = 0; i < samplePayload.Length; i++) samplePayload[i] = (byte)(i % 256);

        await writer.WriteFrameAsync(stream, MessageType.ScreenFrame, samplePayload);

        // Reset stream position for reading
        stream.Position = 0;

        var (type, length) = await NetworkFrameReader.ReadFrameHeaderAsync(stream);
        Assert.Equal(MessageType.ScreenFrame, type);
        Assert.Equal(samplePayload.Length, length);

        byte[] readPayload = await NetworkFrameReader.ReadPayloadAsync(stream, length);
        Assert.Equal(samplePayload, readPayload);
    }

    [Fact]
    public async Task FrameReader_Handles_Fragmented_Stream_Defensively()
    {
        var writer = new NetworkFrameWriter();
        using var ms = new MemoryStream();

        byte[] payload = new byte[256];
        new Random(42).NextBytes(payload);
        await writer.WriteFrameAsync(ms, MessageType.ScreenFrame, payload);

        byte[] fullStreamBytes = ms.ToArray();

        // Wrap in a custom fragmented stream that only returns 3 bytes per read call
        using var fragmentedStream = new FragmentedStream(fullStreamBytes, chunkSize: 3);

        var (type, length) = await NetworkFrameReader.ReadFrameHeaderAsync(fragmentedStream);
        Assert.Equal(MessageType.ScreenFrame, type);
        Assert.Equal(payload.Length, length);

        byte[] receivedPayload = await NetworkFrameReader.ReadPayloadAsync(fragmentedStream, length);
        Assert.Equal(payload, receivedPayload);
    }

    [Fact]
    public async Task FrameReader_Rejects_Oversized_Payloads()
    {
        using var ms = new MemoryStream();
        ms.WriteByte((byte)MessageType.ScreenFrame);
        // Write length larger than ProtocolConstants.MaxPayloadSize (20MB)
        int oversized = ProtocolConstants.MaxPayloadSize + 1024;
        byte[] lenBytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(lenBytes, oversized);
        ms.Write(lenBytes);

        ms.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await NetworkFrameReader.ReadFrameHeaderAsync(ms);
        });
    }

    [Theory]
    [InlineData(PowerActionType.Lock)]
    [InlineData(PowerActionType.Sleep)]
    [InlineData(PowerActionType.Restart)]
    [InlineData(PowerActionType.Shutdown)]
    public void PowerActionMessage_RoundTrip_Serialization(PowerActionType actionType)
    {
        var original = new PowerActionMessage { Action = actionType };
        byte[] bytes = original.Serialize();
        var deserialized = PowerActionMessage.Deserialize(bytes);

        Assert.Equal(original.Action, deserialized.Action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SimplePassword123")]
    [InlineData("P@ssw0rd!#$*&()-_=+{}[]|:;<>?,./~`")]
    [InlineData("UnicodePassword!🔑🚀💻")]
    public void UnlockWithOsPasswordMessage_RoundTrip_Serialization(string password)
    {
        var original = new UnlockWithOsPasswordMessage { Password = password };
        byte[] bytes = original.Serialize();
        var deserialized = UnlockWithOsPasswordMessage.Deserialize(bytes);

        Assert.Equal(original.Password, deserialized.Password);
    }

    private sealed class FragmentedStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunkSize;
        private int _position;

        public FragmentedStream(byte[] data, int chunkSize)
        {
            _data = data;
            _chunkSize = chunkSize;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _data.Length) return 0;
            int toRead = Math.Min(Math.Min(count, _chunkSize), _data.Length - _position);
            Array.Copy(_data, _position, buffer, offset, toRead);
            _position += toRead;
            return toRead;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _data.Length) return ValueTask.FromResult(0);
            int toRead = Math.Min(Math.Min(buffer.Length, _chunkSize), _data.Length - _position);
            _data.AsSpan(_position, toRead).CopyTo(buffer.Span);
            _position += toRead;
            return ValueTask.FromResult(toRead);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public class ChatMessageProtocolTests
{
    [Fact]
    public void ChatMessage_RoundTrip_Serialization()
    {
        var original = new RemoteLAN.Protocol.Messages.ChatMessagePayload
        {
            SenderName = "DESKTOP-TEST",
            TimestampUtcMs = 1_000_000_000L,
            Text = "Hello from the Controller!"
        };

        byte[] bytes = original.Serialize();
        var deserialized = RemoteLAN.Protocol.Messages.ChatMessagePayload.Deserialize(bytes);

        Assert.Equal(original.SenderName, deserialized.SenderName);
        Assert.Equal(original.TimestampUtcMs, deserialized.TimestampUtcMs);
        Assert.Equal(original.Text, deserialized.Text);
    }

    [Fact]
    public void ChatMessage_EmptyText_SerializesAndDeserializesCorrectly()
    {
        var original = new RemoteLAN.Protocol.Messages.ChatMessagePayload
        {
            SenderName = "AGENT-PC",
            TimestampUtcMs = 9_999_999L,
            Text = string.Empty
        };

        byte[] bytes = original.Serialize();
        var deserialized = RemoteLAN.Protocol.Messages.ChatMessagePayload.Deserialize(bytes);

        Assert.Equal(string.Empty, deserialized.Text);
        Assert.Equal("AGENT-PC", deserialized.SenderName);
    }

    [Fact]
    public void ChatMessage_MaxLength_Enforced_On_Deserialize()
    {
        // Build a payload that exceeds MaxTextLength to verify the guard fires
        string oversized = new('A', RemoteLAN.Protocol.Messages.ChatMessagePayload.MaxTextLength + 1);

        var msg = new RemoteLAN.Protocol.Messages.ChatMessagePayload
        {
            SenderName = "Attacker",
            TimestampUtcMs = 0L,
            Text = oversized
        };

        byte[] bytes = msg.Serialize();
        Assert.Throws<System.IO.InvalidDataException>(() =>
            RemoteLAN.Protocol.Messages.ChatMessagePayload.Deserialize(bytes));
    }

    [Fact]
    public void ChatMessage_MessageType_Values_Are_Correct()
    {
        Assert.Equal((byte)0x40, (byte)RemoteLAN.Protocol.Messages.MessageType.ChatMessage);
        Assert.Equal((byte)0x41, (byte)RemoteLAN.Protocol.Messages.MessageType.ChatTypingIndicator);
    }
}

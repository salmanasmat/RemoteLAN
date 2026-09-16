using System.IO;

namespace RemoteLAN.Protocol.Messages;

public enum MouseButtonType : byte
{
    Left = 0,
    Right = 1,
    Middle = 2
}

public enum MouseButtonAction : byte
{
    Down = 0,
    Up = 1
}

public enum KeyAction : byte
{
    Down = 0,
    Up = 1
}

public sealed class MouseMoveMessage
{
    public double NormalizedX { get; set; }
    public double NormalizedY { get; set; }

    public byte[] Serialize()
    {
        using var ms = new MemoryStream(16);
        using var writer = new BinaryWriter(ms);
        writer.Write(NormalizedX);
        writer.Write(NormalizedY);
        return ms.ToArray();
    }

    public static MouseMoveMessage Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);
        return new MouseMoveMessage
        {
            NormalizedX = reader.ReadDouble(),
            NormalizedY = reader.ReadDouble()
        };
    }
}

public sealed class MouseButtonMessage
{
    public MouseButtonType Button { get; set; }
    public MouseButtonAction Action { get; set; }

    public byte[] Serialize()
    {
        return new[] { (byte)Button, (byte)Action };
    }

    public static MouseButtonMessage Deserialize(byte[] data)
    {
        return new MouseButtonMessage
        {
            Button = (MouseButtonType)data[0],
            Action = (MouseButtonAction)data[1]
        };
    }
}

public sealed class MouseWheelMessage
{
    public int Delta { get; set; }

    public byte[] Serialize()
    {
        using var ms = new MemoryStream(4);
        using var writer = new BinaryWriter(ms);
        writer.Write(Delta);
        return ms.ToArray();
    }

    public static MouseWheelMessage Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);
        return new MouseWheelMessage
        {
            Delta = reader.ReadInt32()
        };
    }
}

public sealed class KeyboardKeyMessage
{
    public int VirtualKeyCode { get; set; }
    public KeyAction Action { get; set; }
    public bool IsExtendedKey { get; set; }

    public byte[] Serialize()
    {
        using var ms = new MemoryStream(6);
        using var writer = new BinaryWriter(ms);
        writer.Write(VirtualKeyCode);
        writer.Write((byte)Action);
        writer.Write(IsExtendedKey);
        return ms.ToArray();
    }

    public static KeyboardKeyMessage Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);
        return new KeyboardKeyMessage
        {
            VirtualKeyCode = reader.ReadInt32(),
            Action = (KeyAction)reader.ReadByte(),
            IsExtendedKey = reader.ReadBoolean()
        };
    }
}

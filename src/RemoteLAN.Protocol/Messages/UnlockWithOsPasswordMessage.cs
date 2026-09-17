using System.Text;

namespace RemoteLAN.Protocol.Messages;

public sealed class UnlockWithOsPasswordMessage
{
    public string Password { get; set; } = string.Empty;

    public byte[] Serialize()
    {
        if (string.IsNullOrEmpty(Password))
        {
            return [];
        }
        return Encoding.UTF8.GetBytes(Password);
    }

    public static UnlockWithOsPasswordMessage Deserialize(byte[]? data)
    {
        if (data == null || data.Length == 0)
        {
            return new UnlockWithOsPasswordMessage { Password = string.Empty };
        }
        return new UnlockWithOsPasswordMessage
        {
            Password = Encoding.UTF8.GetString(data)
        };
    }
}

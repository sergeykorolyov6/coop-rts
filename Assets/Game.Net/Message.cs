using System.IO;
using System.Text;

namespace Game.Net;

// Building and opening messages: the type byte, then the fields in BinaryWriter's format.
internal static class Message
{
    public static byte[] Build(MessageType type, System.Action<BinaryWriter>? fields = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)type);
            fields?.Invoke(writer);
        }
        return stream.ToArray();
    }

    public static BinaryReader Open(byte[] message, out MessageType type)
    {
        var reader = new BinaryReader(new MemoryStream(message, writable: false), Encoding.UTF8);
        type = (MessageType)reader.ReadByte();
        return reader;
    }

    public static void WriteBytes(BinaryWriter writer, byte[] bytes)
    {
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    public static byte[] ReadBytes(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > LockstepSettings.MaxMessageBytes)
            throw new InvalidDataException($"Bad length {length}.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();
        return bytes;
    }
}

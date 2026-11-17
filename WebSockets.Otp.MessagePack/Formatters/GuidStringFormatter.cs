using MessagePack;
using System.Buffers;
using MessagePack.Formatters;

namespace WebSockets.Otp.MessagePack.Formatters;

public sealed class GuidStringFormatter : IMessagePackFormatter<Guid>
{
    public static readonly GuidStringFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, Guid value, MessagePackSerializerOptions options)
    {
        writer.Write(value.ToString("D"));
    }

    public Guid Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil()) return Guid.Empty;

        var code = reader.NextMessagePackType;
        switch (code)
        {
            case MessagePackType.String:
                var s = reader.ReadString();
                return Guid.Parse(s);

            case MessagePackType.Binary:
                var bytes = reader.ReadBytes();
                if (bytes is null || bytes.Value.Length != 16)
                    throw new MessagePackSerializationException($"Guid bin must be 16 bytes, got {bytes?.Length ?? 0}.");
                return new Guid(bytes.Value.ToArray());

            default:
                throw new MessagePackSerializationException($"Guid: unexpected msgpack type {code} (code 0x{reader.NextCode:X2}).");
        }
    }
}

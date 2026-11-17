using MessagePack;
using MessagePack.Formatters;
using System.Globalization;

namespace WebSockets.Otp.MessagePack.Formatters;

public sealed class DateTimeOffsetStringFormatter : IMessagePackFormatter<DateTimeOffset>
{
    public static readonly DateTimeOffsetStringFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, DateTimeOffset value, MessagePackSerializerOptions options)
    {
        writer.Write(value.ToString("o", CultureInfo.InvariantCulture));
    }

    public DateTimeOffset Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil()) return default;

        var code = reader.NextMessagePackType;
        switch (code)
        {
            case MessagePackType.String:
                var s = reader.ReadString();
                return DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            case MessagePackType.Extension:
                return reader.ReadDateTime();

            case MessagePackType.Integer:
                return DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());

            default:
                throw new MessagePackSerializationException(
                    $"DateTimeOffset: unexpected msgpack type {code} (code 0x{reader.NextCode:X2}).");
        }
    }
}
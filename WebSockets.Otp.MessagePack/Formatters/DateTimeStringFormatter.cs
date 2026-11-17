using MessagePack;
using MessagePack.Formatters;
using System.Globalization;

namespace WebSockets.Otp.MessagePack.Formatters;

public sealed class DateTimeStringFormatter : IMessagePackFormatter<DateTime>
{
    public static readonly DateTimeStringFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, DateTime value, MessagePackSerializerOptions options)
        => writer.Write(value.ToString("o", CultureInfo.InvariantCulture));

    public DateTime Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil()) return default;
        var code = reader.NextMessagePackType;

        if (code == MessagePackType.String)
            return DateTime.Parse(reader.ReadString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        if (code == MessagePackType.Extension) 
            return reader.ReadDateTime();

        if (code == MessagePackType.Integer) 
            return DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64()).UtcDateTime;

        throw new MessagePackSerializationException($"DateTime: unexpected type {code}.");
    }
}

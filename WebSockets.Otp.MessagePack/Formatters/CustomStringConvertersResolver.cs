using MessagePack;
using MessagePack.Formatters;

namespace WebSockets.Otp.MessagePack.Formatters;

public sealed class CustomStringConvertersResolver : IFormatterResolver
{
    public static readonly CustomStringConvertersResolver Instance = new();

    public IMessagePackFormatter<T>? GetFormatter<T>()
    {
        if (typeof(T) == typeof(Guid)) 
            return (IMessagePackFormatter<T>)(object)GuidStringFormatter.Instance;
        if (typeof(T) == typeof(DateTimeOffset))
            return (IMessagePackFormatter<T>)(object)DateTimeOffsetStringFormatter.Instance;
        return null;
    }
}

using System.Text.Json;

namespace WebSockets.Otp.Abstractions.Options;

public readonly record struct MessageHeaders(
    uint Id,
    ReadOnlyMemory<byte> Key,
    ReadOnlyMemory<byte> Value);

public readonly record struct MessageType(byte Value)
{
    public static readonly MessageType Response = new(1);
    public static readonly MessageType Error = new(2);
    public static readonly MessageType Event = new(3);
}

public abstract record WsMessage
{
    public abstract MessageType Type { get; }

    public abstract void WritePayload(Utf8JsonWriter writer, JsonSerializerOptions options);
}

public sealed record ResponseMessage<T>(uint Id, T Value) : WsMessage
    where T : notnull
{
    public override MessageType Type => MessageType.Response;

    public override void WritePayload(Utf8JsonWriter writer, JsonSerializerOptions options)
    {
        writer.WriteNumber("id", Id);
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, Value, options);
    }
}

public sealed record ErrorMessage(uint Id, string Error) : WsMessage
{
    public override MessageType Type => MessageType.Error;

    public override void WritePayload(Utf8JsonWriter writer, JsonSerializerOptions options)
    {
        writer.WriteNumber("id", Id);
        writer.WritePropertyName("error");
        JsonSerializer.Serialize(writer, Error, options);
    }
}

public sealed record EventMessage<T>(string Key, T Value) : WsMessage
    where T : notnull
{
    public override MessageType Type => MessageType.Event;

    public override void WritePayload(Utf8JsonWriter writer, JsonSerializerOptions options)
    {
        writer.WriteString("key", Key);
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, Value, options);
    }
}
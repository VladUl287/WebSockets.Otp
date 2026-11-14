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
}

public sealed record ResponseMessage(uint Id, object Value) : WsMessage
{
    public override MessageType Type => MessageType.Response;
}

public sealed record ErrorMessage(uint Id, string Error) : WsMessage
{
    public override MessageType Type => MessageType.Error;
}

public sealed record EventMessage(string Key, object Value) : WsMessage
{
    public override MessageType Type => MessageType.Event;
}
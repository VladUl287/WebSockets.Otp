namespace WebSockets.Otp.Abstractions.Connection;

public readonly record struct MessageHeaders(
    uint CorrelationId,
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

public sealed record ResponseMessage(uint CorrelationId, object Value) : WsMessage
{
    public override MessageType Type => MessageType.Response;
}

public enum WsErrorCode
{
    InvalidPayload,
    UnknownRoute,
    HandlerFailed,
    Unauthorized,
    RateLimited,
    Internal
}

public sealed record ErrorMessage(uint CorrelationId, WsErrorCode Code, string Message) : WsMessage
{
    public override MessageType Type => MessageType.Error;
}

public sealed record EventMessage(string Key, object Value) : WsMessage
{
    public override MessageType Type => MessageType.Event;
}
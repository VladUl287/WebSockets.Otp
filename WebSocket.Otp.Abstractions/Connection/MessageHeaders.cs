namespace WebSockets.Otp.Abstractions.Connection;

public readonly record struct CorrelationId(uint Value) : ISpanFormattable, IUtf8SpanFormattable
{
    public static readonly CorrelationId Empty = default;

    public bool IsEmpty => Value == 0;
    public bool HasValue => Value > 0;

    public static CorrelationId FromUInt32(uint value) => new(value);

    public static explicit operator CorrelationId(uint value) => new(value);
    public static implicit operator uint(CorrelationId id) => id.Value;

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider)
        => Value.ToString(format, formatProvider);

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => Value.TryFormat(destination, out charsWritten, format, provider);

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => 
        Value.TryFormat(utf8Destination, out bytesWritten, format, provider);
}

public readonly record struct MessageHeaders(
    CorrelationId CorrelationId,
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
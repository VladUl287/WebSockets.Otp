using System.Diagnostics.CodeAnalysis;

namespace WebSockets.Otp.Abstractions.Utils;

public readonly record struct EndpointResult<T>
{
    public T? Value { get; }
    public string? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsSuccess => Error is null;

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsError => Error is not null;

    private EndpointResult(T? value, string? error)
    {
        Value = value;
        Error = error;
    }

    public static EndpointResult<T> Ok(T value) => new(value, null);
    public static EndpointResult<T> Fail(string message) => new(default, message);
}

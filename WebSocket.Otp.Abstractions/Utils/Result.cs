using WebSockets.Otp.Abstractions.Options;

namespace WebSockets.Otp.Abstractions.Utils;

public readonly record struct Result<T>
{
    public T? Value { get; }
    public WsError? Error { get; }

    public bool IsSuccess => Error is null;
    public bool IsError => Error is not null;

    private Result(T? value, WsError? error)
    {
        Value = value;
        Error = error;
    }

    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Fail(WsError error) => new(default, error);
    public static Result<T> Fail(WsErrorCode code, string message) => new(default, new WsError(code, message));
}

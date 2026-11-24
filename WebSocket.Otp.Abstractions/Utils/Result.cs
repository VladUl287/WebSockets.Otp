namespace WebSockets.Otp.Abstractions.Utils;

public readonly struct Success { }
public readonly struct Error { }
public readonly struct Error<T>(T value)
{
    public T Value => value;
}

public readonly struct Result<TSuccess, TError>
{
    private readonly TSuccess? _success;
    private readonly TError? _error;
    private readonly int _index;

    private Result(int index, TSuccess? success = default, TError? error = default)
    {
        _index = index;
        _success = success;
        _error = error;
    }

    public bool IsSuccess => _index == 1;
    public bool IsError => _index == 2;

    public TSuccess Value =>
       _index == 1 ? _success! : throw new InvalidOperationException($"Cannot return as First as result is Second");
    public TError Error =>
       _index == 2 ? _error! : throw new InvalidOperationException($"Cannot return as Second as result is First");

    public static Result<TSuccess, TError> Success(TSuccess value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        return new(1, value);
    }

    public static Result<TSuccess, TError> Fail(TError error)
    {
        if (error is null) throw new ArgumentNullException(nameof(error));
        return new(2, default, error);
    }

    public static implicit operator Result<TSuccess, TError>(TSuccess value) => Success(value);
    public static implicit operator Result<TSuccess, TError>(TError error) => Fail(error);
}
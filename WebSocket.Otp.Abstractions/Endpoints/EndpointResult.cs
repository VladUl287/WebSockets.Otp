namespace WebSockets.Otp.Abstractions.Endpoints;

public readonly record struct EndpointResult<T>
{
    private readonly T? _success;
    private readonly string? _error;
    private readonly int _index;

    private EndpointResult(int index, T? success = default, string? error = default)
    {
        _index = index;
        _success = success;
        _error = error;
    }

    public bool IsSuccess => _index == 1;
    public bool IsError => _index == 2;

    public T Value =>
       _index == 1 ? _success! : throw new InvalidOperationException($"Cannot return as First as result is Second");
    public string Error =>
       _index == 2 ? _error! : throw new InvalidOperationException($"Cannot return as Second as result is First");

    public static EndpointResult<T> Ok(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(1, value);
    }

    public static EndpointResult<T> Fail(string error)
    {
        ArgumentException.ThrowIfNullOrEmpty(error);
        return new(2, default, error);
    }

    public static implicit operator EndpointResult<T>(T value) => Ok(value);
    public static implicit operator EndpointResult<T>(string error) => Fail(error);
}
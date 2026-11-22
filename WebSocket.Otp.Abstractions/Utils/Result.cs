using System.Diagnostics.CodeAnalysis;

namespace WebSockets.Otp.Abstractions.Utils;

public readonly struct Success { }
public readonly struct Error { }

public readonly struct Result<TSuccess, TError>(int index, TSuccess? success = default, TError? error = default)
{
    public int Index => index;

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => index == 0;

    [MemberNotNullWhen(true, nameof(Error))]
    [MemberNotNullWhen(false, nameof(Value))]
    public bool IsError => index == 1;

    public TSuccess? Value => success;
    public TError? Error => error;

    public static implicit operator Result<TSuccess, TError>(TSuccess t) => new(0, t);
    public static implicit operator Result<TSuccess, TError>(TError t) => new(1, default, t);
}
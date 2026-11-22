using System.Diagnostics.CodeAnalysis;

namespace WebSockets.Otp.Abstractions.Utils;

public readonly struct Success { }
public readonly struct Error { }

public readonly struct Result<TValue, TError>(TValue? value, TError? error)
{
    public TValue? Value => value;
    public TError? Error => error;

    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsSuccess => value is not null;

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsError => error is not null;

    public static implicit operator Result<TValue, TError>(TValue t) => new(t, default);
    public static implicit operator Result<TValue, TError>(TError t) => new(default, t);
}
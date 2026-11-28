namespace WebSockets.Otp.Abstractions.Utils;

public readonly record struct WsTarget
{
    public bool IsBroadcast { get; private init; }
    public IReadOnlyList<string> Connections { get; private init; }
    public IReadOnlyList<string> Groups { get; private init; }
    public IReadOnlyList<string> Exclude { get; private init; }

    public bool IsEmpty => !IsBroadcast && Connections is { Count: 0 } && Groups is { Count: 0 };

    public WsTarget(
        bool isBroadcast,
        IReadOnlyList<string>? connections,
        IReadOnlyList<string>? groups,
        IReadOnlyList<string>? exclude)
    {
        IsBroadcast = isBroadcast;
        Connections = connections ?? [];
        Groups = groups ?? [];
        Exclude = exclude ?? [];
    }
}

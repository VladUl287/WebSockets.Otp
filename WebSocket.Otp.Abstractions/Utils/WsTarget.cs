using System.Diagnostics.CodeAnalysis;

namespace WebSockets.Otp.Abstractions.Utils;

public readonly record struct WsTarget
{
    public string? Connection { get; }
    public string? Group { get; }
    public IReadOnlyList<string> Exclude { get; }

    public bool IsEmpty => Connection is null && Group is null;

    [MemberNotNullWhen(true, nameof(Connection))]
    public bool IsConnection => Connection is not null;

    [MemberNotNullWhen(true, nameof(Group))]
    public bool IsGroup => Group is not null;

    private WsTarget(string? connection, string? group, IReadOnlyList<string>? exclude)
    {
        Connection = connection;
        Group = group;
        Exclude = exclude ?? [];
    }

    public static WsTarget ToConnection(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        return new(connectionId, null, null);
    }

    public static WsTarget ToGroup(string groupName, IReadOnlyList<string>? exclude = null)
    {
        ArgumentNullException.ThrowIfNull(groupName);
        return new(null, groupName, exclude);
    }
}

public readonly record struct WsTargets
{
    public bool IsBroadcast { get; }
    public IReadOnlyList<string> Connections { get; }
    public IReadOnlyList<string> Groups { get; }
    public IReadOnlyList<string> Exclude { get; }

    public bool IsEmpty => !IsBroadcast && Connections is null or { Count: 0 } && Groups is null or { Count: 0 };

    public WsTargets(
        bool isBroadcast = false,
        IReadOnlyList<string>? connections = null,
        IReadOnlyList<string>? groups = null,
        IReadOnlyList<string>? exclude = null)
    {
        IsBroadcast = isBroadcast;
        Connections = connections ?? [];
        Groups = groups ?? [];
        Exclude = exclude ?? [];
    }
}

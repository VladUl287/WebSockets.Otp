using System.Diagnostics.CodeAnalysis;

namespace WebSockets.Otp.Abstractions.Utils;

public readonly record struct WsTarget
{
    public bool IsBroadcast { get; }
    public string? Connection { get; }
    public string? Group { get; }
    public IReadOnlyList<string> Connections { get; }
    public IReadOnlyList<string> Groups { get; }
    public IReadOnlyList<string> Exclude { get; }

    public bool IsEmpty => 
        !IsBroadcast && Connection is null && Group is null && Connections is null or { Count: 0 } && Groups is null or { Count: 0 };

    [MemberNotNullWhen(true, nameof(Connection))]
    public bool IsSingleConnection => Connection is not null;

    [MemberNotNullWhen(true, nameof(Group))]
    public bool IsSingleGroup => Group is not null;

    private WsTarget(
        bool isBroadcast,
        string? connection,
        string? group,
        IReadOnlyList<string>? connections,
        IReadOnlyList<string>? groups,
        IReadOnlyList<string>? exclude)
    {
        IsBroadcast = isBroadcast;
        Connection = connection;
        Group = group;
        Connections = connections ?? [];
        Groups = groups ?? [];
        Exclude = exclude ?? [];
    }

    public static WsTarget Broadcast(IReadOnlyList<string>? exclude = null) 
        => new(true, null, null, null, null, exclude);

    public static WsTarget ToConnection(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        return new(false, connectionId, null, null, null, null);
    }

    public static WsTarget ToGroup(string groupName, IReadOnlyList<string>? exclude = null)
    {
        ArgumentNullException.ThrowIfNull(groupName);
        return new(false, null, groupName, null, null, exclude);
    }

    public static WsTarget ToMany(
        IReadOnlyList<string>? connections = null, 
        IReadOnlyList<string>? groups = null,
        IReadOnlyList<string>? exclude = null)
        => new(false, null, null, connections, groups, exclude);
}

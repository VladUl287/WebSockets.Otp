using System.Collections.Immutable;

namespace WebSockets.Otp.Abstractions.Utils;

public readonly record struct WsTarget
{
    private static readonly ImmutableArray<string> Empty = [];

    public bool IsBroadcast { get; }

    public string? SingleConnection { get; }
    private readonly ImmutableArray<string> _connections;
    public ImmutableArray<string> Connections => _connections.IsDefault ? Empty : _connections;

    public string? SingleGroup { get; }
    private readonly ImmutableArray<string> _groups;
    public ImmutableArray<string> Groups => _groups.IsDefault ? Empty : _groups;

    public string? SingleExclude { get; }
    private readonly ImmutableArray<string> _excluded;
    public ImmutableArray<string> ExcludedConnections => _excluded.IsDefault ? Empty : _excluded;

    public bool IsEmpty =>
        !IsBroadcast && SingleConnection is null && Connections.IsEmpty && SingleGroup is null && Groups.IsEmpty;

    private WsTarget(
        bool isBroadcast,
        string? singleConnection,
        ImmutableArray<string> connections,
        string? singleGroup,
        ImmutableArray<string> groups,
        string? singleExclude,
        ImmutableArray<string> excludedConnections)
    {
        IsBroadcast = isBroadcast;
        SingleConnection = singleConnection;
        _connections = connections;
        SingleGroup = singleGroup;
        _groups = groups;
        SingleExclude = singleExclude;
        _excluded = excludedConnections;
    }

    public static WsTarget All() =>
        new(isBroadcast: true, default, default, default, default, default, default);

    public static WsTarget Connection(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        return new(false, connectionId, default, null, default, null, default);
    }

    public static WsTarget Connection(IEnumerable<string> connectionIds)
    {
        ArgumentNullException.ThrowIfNull(connectionIds);
        return new(false, null, [.. connectionIds], null, default, null, default);
    }

    public static WsTarget Group(string group)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        return new(false, null, default, group, default, null, default);
    }

    public static WsTarget Group(IEnumerable<string> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return new(false, null, default, null, [.. groups], null, default);
    }

    public WsTarget AndConnection(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        return new(
            IsBroadcast, SingleConnection, Connections.Add(connectionId), SingleGroup, Groups, SingleExclude, ExcludedConnections);
    }

    public WsTarget AndConnections(IEnumerable<string> connectionIds)
    {
        ArgumentNullException.ThrowIfNull(connectionIds);

        var added = connectionIds.ToImmutableArray();
        if (added.IsEmpty)
            return this;

        return new(
            IsBroadcast, SingleConnection, Connections.AddRange(added), SingleGroup, Groups, SingleExclude, ExcludedConnections);
    }

    public WsTarget AndGroup(string group)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        return new(
            IsBroadcast, SingleConnection, Connections, SingleGroup, Groups.Add(group), SingleExclude, ExcludedConnections);
    }

    public WsTarget AndGroups(IEnumerable<string> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var added = groups.ToImmutableArray();
        if (added.IsEmpty)
            return this;

        return new(
            IsBroadcast, SingleConnection, Connections, SingleGroup, Groups.AddRange(added), SingleExclude, ExcludedConnections);
    }

    public WsTarget Except(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        return new(
            IsBroadcast, SingleConnection, Connections, SingleGroup, Groups, SingleExclude, ExcludedConnections.Add(connectionId));
    }

    public WsTarget Except(IEnumerable<string> connectionIds)
    {
        ArgumentNullException.ThrowIfNull(connectionIds);

        var added = connectionIds.ToImmutableArray();
        if (added.IsEmpty)
            return this;

        return new(
            IsBroadcast, SingleConnection, Connections, SingleGroup, Groups, SingleExclude, ExcludedConnections.AddRange(added));
    }
}

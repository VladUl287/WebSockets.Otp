using MessagePack;
using MessagePack.Resolvers;
using StackExchange.Redis;
using System.Buffers;
using System.Collections.Concurrent;
using WebSockets.Otp.Abstractions.Connection;

namespace WebSockets.Otp.Redis;

public sealed class RedisConnectionManager : IWsConnectionManager, IAsyncDisposable
{
    private const string DirectChannel = "ws:pubsub:direct";
    private const string GroupChannel = "ws:pubsub:group";
    private const string ConnectionsSetKey = "ws:connections";
    private const string ConnHashPrefix = "ws:connection:";
    private const string GroupSetPrefix = "ws:group:";
    private const string ConnGroupsPrefix = "ws:conn_groups:";

    private static readonly MessagePackSerializerOptions EnvelopeOptions =
        MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance)
            .WithSecurity(MessagePackSecurity.UntrustedData);

    private static readonly MessagePackSerializerOptions PayloadOptions = 
        MessagePackSerializer.Typeless.DefaultOptions
            .WithSecurity(MessagePackSecurity.UntrustedData);

    [MessagePackObject(AllowPrivate = true)]
    internal sealed record Envelope(
        [property: Key(0)] string Kind,
        [property: Key(1)] byte[] Data,
        [property: Key(2)] string[] Targets);

    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ISubscriber _sub;
    private readonly RedisChannel _directRedisChannel;
    private readonly RedisChannel _groupRedisChannel;

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IWsConnection>> _localGroups = new();
    private readonly ConcurrentDictionary<string, IWsConnection> _localConnections = new();

    public RedisConnectionManager(IConnectionMultiplexer redis)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _db = redis.GetDatabase();
        _sub = redis.GetSubscriber();

        _directRedisChannel = RedisChannel.Literal(DirectChannel);
        _groupRedisChannel = RedisChannel.Literal(GroupChannel);

        _sub.Subscribe(_directRedisChannel, (_, msg) => OnPubSubMessageAsync(msg, "direct"));
        _sub.Subscribe(_groupRedisChannel, (_, msg) => OnPubSubMessageAsync(msg, "group"));
    }

    public async ValueTask<bool> TryAdd(IWsConnection connection, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var added = await _db.SetAddAsync(ConnectionsSetKey, connection.Id);
        if (!added) return false;

        _localConnections[connection.Id] = connection;

        await _db.HashSetAsync(ConnHashPrefix + connection.Id,
        [
            new("id", connection.Id),
            new("connectedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
        ]);

        return true;
    }

    public async ValueTask<bool> TryRemove(string connectionId, CancellationToken token)
    {
        if (string.IsNullOrEmpty(connectionId)) return false;

        var groups = await _db.SetMembersAsync(ConnGroupsPrefix + connectionId);
        if (groups.Length > 0)
        {
            var batch = _db.CreateBatch();
            var tasks = new List<Task>(groups.Length * 2);
            foreach (var g in groups)
            {
                var grp = (string)g!;
                tasks.Add(batch.SetRemoveAsync(GroupSetPrefix + grp, connectionId));
                tasks.Add(batch.SetRemoveAsync(ConnGroupsPrefix + connectionId, grp));

                if (_localGroups.TryGetValue(grp, out var members))
                    members.TryRemove(connectionId, out _);
            }
            batch.Execute();
            await Task.WhenAll(tasks);
        }

        var removed = await _db.SetRemoveAsync(ConnectionsSetKey, connectionId);

        _localConnections.TryRemove(connectionId, out _);

        await Task.WhenAll(
            _db.KeyDeleteAsync(ConnHashPrefix + connectionId),
            _db.KeyDeleteAsync(ConnGroupsPrefix + connectionId));

        return removed;
    }

    public async ValueTask<bool> AddToGroup(string group, string connectionId, CancellationToken token)
    {
        if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(connectionId))
            return false;

        var tran = _db.CreateTransaction();
        _ = tran.SetAddAsync(GroupSetPrefix + group, connectionId);
        _ = tran.SetAddAsync(ConnGroupsPrefix + connectionId, group);
        var ok = await tran.ExecuteAsync();

        if (ok && _localConnections.TryGetValue(connectionId, out var conn))
        {
            var members = _localGroups.GetOrAdd(group, _ => new());
            members[connectionId] = conn;
        }

        return ok;
    }

    public async ValueTask<bool> RemoveFromGroup(string group, string connectionId, CancellationToken token)
    {
        if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(connectionId))
            return false;

        var tran = _db.CreateTransaction();
        _ = tran.SetRemoveAsync(GroupSetPrefix + group, connectionId);
        _ = tran.SetRemoveAsync(ConnGroupsPrefix + connectionId, group);
        var ok = await tran.ExecuteAsync();

        if (_localGroups.TryGetValue(group, out var members))
        {
            members.TryRemove(connectionId, out _);

            if (members.IsEmpty)
            {
                _localGroups.TryRemove(group, out _);
            }
        }

        return ok;
    }

    public ValueTask Send(WsMessage data, CancellationToken token) =>
        BroadcastAsync(data, token);

    public ValueTask Send(string connectionId, WsMessage data, CancellationToken token)
    {
        if (string.IsNullOrEmpty(connectionId))
            return ValueTask.CompletedTask;

        return PublishAsync("direct", data, [connectionId]);
    }

    public ValueTask Send(IEnumerable<string> connections, WsMessage data, CancellationToken token)
    {
        var targets = connections?.Where(c => !string.IsNullOrEmpty(c)).Distinct().ToArray() ?? [];
        if (targets.Length == 0)
            return ValueTask.CompletedTask;

        return PublishAsync("direct", data, targets);
    }

    public ValueTask SendToGroup(string group, WsMessage data, CancellationToken token)
    {
        if (string.IsNullOrEmpty(group))
            return ValueTask.CompletedTask;

        return PublishAsync("group", data, [group]);
    }

    public ValueTask SendToGroup(IEnumerable<string> groups, WsMessage data, CancellationToken token)
    {
        var targets = groups?.Where(g => !string.IsNullOrEmpty(g)).Distinct().ToArray() ?? [];
        if (targets.Length == 0)
            return ValueTask.CompletedTask;

        return PublishAsync("group", data, targets);
    }

    private async ValueTask BroadcastAsync(WsMessage data, CancellationToken token)
    {
        var ids = await _db.SetMembersAsync(ConnectionsSetKey);
        if (ids.Length == 0) return;

        var targets = new string[ids.Length];
        for (int i = 0; i < ids.Length; i++) targets[i] = ids[i]!;
        await PublishAsync("direct", data, targets);
    }

    private async ValueTask PublishAsync(string kind, WsMessage data, string[] targets)
    {
        var dataBytes = MessagePackSerializer.Typeless.Serialize(data, PayloadOptions);

        var envelope = new Envelope(kind, dataBytes, targets);
        var payload = MessagePackSerializer.Serialize(envelope, EnvelopeOptions);

        var channel = kind == "direct" ? _directRedisChannel : _groupRedisChannel;
        await _sub.PublishAsync(channel, payload);
    }

    private async Task OnPubSubMessageAsync(RedisValue message, string expectedKind)
    {
        if (message.IsNullOrEmpty) return;

        if ((byte[]?)message is not { } payload) return;

        Envelope? env;
        try
        {
            env = MessagePackSerializer.Deserialize<Envelope>(payload, EnvelopeOptions);
        }
        catch (MessagePackSerializationException) { return; }

        if (env is null || env.Kind != expectedKind || env.Targets.Length == 0) return;

        WsMessage? data;
        try
        {
            data = MessagePackSerializer.Typeless.Deserialize(env.Data, PayloadOptions) as WsMessage;
        }
        catch (MessagePackSerializationException) { return; }

        if (data is null) return;

        foreach (var target in env.Targets)
        {
            var sockets = env.Kind == "direct"
                ? GetLocalDirect(target)
                : GetLocalGroup(target);

            if (sockets.Count == 0) continue;

            foreach (var group in sockets.GroupBy(c => c.Serializer.Protocol))
            {
                var serializer = group.First().Serializer;

                ReadOnlyMemory<byte> bytes;
                try
                {
                    var buffer = new ArrayBufferWriter<byte>(256);
                    serializer.SerializeMessage(data, buffer);
                    bytes = buffer.WrittenMemory;
                }
                catch { continue; }

                foreach (var conn in group)
                {
                    try { await conn.Socket.SendAsync(bytes, conn.Serializer.Type, true, CancellationToken.None); }
                    catch { }
                }
            }
        }
    }

    private List<IWsConnection> GetLocalDirect(string connectionId)
        => _localConnections.TryGetValue(connectionId, out var c) ? [c] : [];

    private List<IWsConnection> GetLocalGroup(string group)
        => _localGroups.TryGetValue(group, out var set) ? [.. set.Values] : [];

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _sub.UnsubscribeAsync(_directRedisChannel);
            await _sub.UnsubscribeAsync(_groupRedisChannel);
        }
        catch { }
    }
}
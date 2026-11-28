using Microsoft.Extensions.ObjectPool;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Utils;

namespace WebSockets.Otp.Core.Services;

public sealed class InMemoryConnectionManager(ObjectPool<ArrayBufferWriter<byte>> objectPool) : IWsConnectionManager
{
    private readonly ConcurrentDictionary<string, IWsConnection> _store = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IWsConnection>> _groups = new();

    public ValueTask<bool> TryAdd(IWsConnection connection, CancellationToken token) => new(_store.TryAdd(connection.Id, connection));
    public ValueTask<bool> TryRemove(string connectionId, CancellationToken token) => new(_store.TryRemove(connectionId, out _));

    public ValueTask<bool> AddToGroup(string group, string connectionId, CancellationToken token)
    {
        var added = _groups
            .GetOrAdd(group, [])
            .TryAdd(connectionId, _store[connectionId]);
        return ValueTask.FromResult(added);
    }

    public ValueTask<bool> RemoveFromGroup(string group, string connectionId, CancellationToken token)
    {
        var removed = _groups
            .GetOrAdd(group, [])
            .TryRemove(connectionId, out _);
        return ValueTask.FromResult(removed);
    }

    private static async Task RunWithCleanup(Task work, ArrayBufferWriter<byte> buffer, ObjectPool<ArrayBufferWriter<byte>> pool)
    {
        try { await work; }
        finally { pool.Return(buffer); }
    }

    public async ValueTask Send(WsTarget target, WsMessage envelope, CancellationToken ct = default)
    {
        if (target.IsSingleConnection)
        {
            await Send(target.Connection, envelope, ct);
            return;
        }

        if (target.IsSingleGroup)
        {
            await SendToGroup(target.Group, envelope, target.Exclude, ct);
            return;
        }

        var requested = new HashSet<string>(target.Connections);
        foreach (var group in target.Groups)
        {
            if (_groups.TryGetValue(group, out var groupConnections))
            {
                foreach (var connectionId in groupConnections.Keys)
                    requested.Add(connectionId);
            }
        }
        if (target.Exclude is { Count: > 0 })
        {
            var excludeRequested = new HashSet<string>(target.Exclude);
            requested.RemoveWhere(excludeRequested.Contains);
        }

        var grouped = _store.Values
            .Where(c => requested.Contains(c.Id))
            .GroupBy(c => c.Serializer);


        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 10,
            CancellationToken = ct
        };

        var tasks = new List<Task>();

        foreach (var connections in grouped)
        {
            var serializer = connections.Key;

            var messageBuffer = objectPool.Get();
            serializer.SerializeMessage(envelope, messageBuffer);

            var sends = Parallel.ForEachAsync(connections, options,
                (conn, ct) => conn.Socket.SendAsync(messageBuffer.WrittenMemory, serializer.Type, true, ct));

            var withCleanup = RunWithCleanup(sends, messageBuffer, objectPool);
            tasks.Add(withCleanup);
        }

        await Task.WhenAll(tasks);
    }

    private ValueTask Send(string connectionId, WsMessage envelope, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(connectionId, out var connection))
            throw new ArgumentException($"Connection with id '{connectionId}' was not found.", nameof(connectionId));

        var messageBuffer = objectPool.Get();
        try
        {
            var serializer = connection.Serializer;
            serializer.SerializeMessage(envelope, messageBuffer);
            return connection.Socket.SendAsync(messageBuffer.WrittenMemory, serializer.Type, true, ct);
        }
        finally
        {
            objectPool.Return(messageBuffer);
        }
    }

    private async ValueTask SendToGroup(string group, WsMessage envelope, IReadOnlyList<string> exclude, CancellationToken ct = default)
    {
        if (!_groups.TryGetValue(group, out var groupMap))
            throw new ArgumentException($"Group with name '{group}' was not found.", nameof(group));

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 10,
            CancellationToken = ct
        };

        var groups = groupMap.Values.GroupBy(c => c.Serializer);
        var excluded = new HashSet<string>(exclude)
            .ToImmutableHashSet();
        var tasks = new List<Task>();

        foreach (var connections in groups)
        {
            var serializer = connections.Key;

            var messageBuffer = objectPool.Get();
            serializer.SerializeMessage(envelope, messageBuffer);

            var sends = Parallel.ForEachAsync(connections, parallelOptions,
                (conn, ct) =>
                {
                    if (excluded.Contains(conn.Id))
                        return ValueTask.CompletedTask;

                    return conn.Socket.SendAsync(messageBuffer.WrittenMemory, serializer.Type, true, ct);
                });

            var withCleanup = RunWithCleanup(sends, messageBuffer, objectPool);
            tasks.Add(withCleanup);
        }

        await Task.WhenAll(tasks);
    }
}

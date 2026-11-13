using Microsoft.Extensions.ObjectPool;
using System.Buffers;
using System.Collections.Concurrent;
using WebSockets.Otp.Abstractions.Connections;
using WebSockets.Otp.Abstractions.Options;

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

    public ValueTask Send(WsMessage envelope, CancellationToken token) =>
        Send(_store.Values.Select(c => c.Id), envelope, token);

    public async ValueTask Send(string connectionId, WsMessage envelope, CancellationToken token)
    {
        var connection = _store[connectionId];
        var serializer = connection.Serializer;

        var buffer = objectPool.Get();
        serializer.SerializeMessage(envelope, buffer);

        await connection.Socket.SendAsync(buffer.WrittenMemory, serializer.Type, true, token);

        objectPool.Return(buffer);
    }

    static async Task RunWithCleanup(Task work, ObjectPool<ArrayBufferWriter<byte>> objectPool, ArrayBufferWriter<byte> buffer)
    {
        try { await work; }
        finally { objectPool.Return(buffer); }
    }

    public async ValueTask Send(IEnumerable<string> connections, WsMessage envelope, CancellationToken token)
    {
        var requested = new HashSet<string>(connections);

        var grouped = _store.Values
            .Where(c => requested.Contains(c.Id))
            .GroupBy(c => c.Serializer);

        var tasks = new List<Task>();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 10,
            CancellationToken = token
        };

        foreach (var group in grouped)
        {
            var serializer = group.Key;
            var buffer = objectPool.Get();
            serializer.SerializeMessage(envelope, buffer);

            var sends = Parallel.ForEachAsync(group, options,
                (conn, ct) => conn.Socket.SendAsync(buffer.WrittenMemory, serializer.Type, true, ct));

            var withCleanup = RunWithCleanup(sends, objectPool, buffer);
            tasks.Add(withCleanup);
        }

        await Task.WhenAll(tasks);
    }

    public async ValueTask SendToGroup(string group, WsMessage envelope, CancellationToken token)
    {
        var grouped = _groups[group].Values
            .GroupBy(c => c.Serializer);

        var tasks = new List<Task>();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 10,
            CancellationToken = token
        };

        foreach (var connections in grouped)
        {
            var serializer = connections.Key;
            var buffer = objectPool.Get();
            serializer.SerializeMessage(envelope, buffer);

            var sends = Parallel.ForEachAsync(connections, options,
                (conn, ct) => conn.Socket.SendAsync(buffer.WrittenMemory, serializer.Type, true, ct));

            var withCleanup = RunWithCleanup(sends, objectPool, buffer);
            tasks.Add(withCleanup);
        }

        await Task.WhenAll(tasks);
    }

    public async ValueTask SendToGroup(IEnumerable<string> groups, WsMessage envelope, CancellationToken token)
    {
        var requested = new HashSet<string>(groups);

        var grouped = _groups
            .Where(c => requested.Contains(c.Key))
            .SelectMany(c => c.Value.Select(c => c.Value))
            .GroupBy(c => c.Serializer);

        var tasks = new List<Task>();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 10,
            CancellationToken = token
        };

        foreach (var connections in grouped)
        {
            var serializer = connections.Key;
            var buffer = objectPool.Get();
            serializer.SerializeMessage(envelope, buffer);

            var sends = Parallel.ForEachAsync(connections, options,
                (conn, ct) => conn.Socket.SendAsync(buffer.WrittenMemory, serializer.Type, true, ct));

            var withCleanup = RunWithCleanup(sends, objectPool, buffer);
            tasks.Add(withCleanup);
        }

        await Task.WhenAll(tasks);
    }
}

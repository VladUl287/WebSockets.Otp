using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.Extensions.ObjectPool;
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

    //public ValueTask Send(WsMessage envelope, SendOptions options = default, CancellationToken ct = default) =>
    //    Send([.. _store.Values.Select(c => c.Id)], envelope, options, ct);

    //public async ValueTask Send(string connectionId, WsMessage envelope, CancellationToken token)
    //{
    //    if (!_store.TryGetValue(connectionId, out var connection))
    //    {
    //        throw new ArgumentException($"Connection with id '{connectionId}' was not found.", nameof(connectionId));
    //    }

    //    var buffer = objectPool.Get();
    //    try
    //    {
    //        var serializer = connection.Serializer;
    //        serializer.SerializeMessage(envelope, buffer);
    //        await connection.Socket.SendAsync(buffer.WrittenMemory, serializer.Type, true, token);
    //    }
    //    finally
    //    {
    //        objectPool.Return(buffer);
    //    }
    //}

    //public async ValueTask Send(IReadOnlyList<string> connections, WsMessage envelope, SendOptions options = default, CancellationToken ct = default)
    //{
    //    var requested = new HashSet<string>(connections);

    //    var grouped = _store.Values
    //        .Where(c => requested.Contains(c.Id))
    //        .GroupBy(c => c.Serializer);

    //    var tasks = new List<Task>();

    //    var parallelOptions = new ParallelOptions
    //    {
    //        MaxDegreeOfParallelism = Environment.ProcessorCount * 10,
    //        CancellationToken = ct
    //    };

    //    foreach (var group in grouped)
    //    {
    //        var serializer = group.Key;
    //        var buffer = objectPool.Get();
    //        serializer.SerializeMessage(envelope, buffer);

    //        var sends = Parallel.ForEachAsync(group, parallelOptions,
    //            (conn, ct) => conn.Socket.SendAsync(buffer.WrittenMemory, serializer.Type, true, ct));

    //        var withCleanup = RunWithCleanup(sends, objectPool, buffer);
    //        tasks.Add(withCleanup);
    //    }

    //    await Task.WhenAll(tasks);
    //}

    //public async ValueTask SendToGroup(string group, WsMessage envelope, SendOptions options = default, CancellationToken ct = default)
    //{
    //    var grouped = _groups[group].Values
    //        .GroupBy(c => c.Serializer);

    //    var tasks = new List<Task>();

    //    var parallelOptions = new ParallelOptions
    //    {
    //        MaxDegreeOfParallelism = Environment.ProcessorCount * 10,
    //        CancellationToken = ct
    //    };

    //    foreach (var connections in grouped)
    //    {
    //        var serializer = connections.Key;
    //        var buffer = objectPool.Get();
    //        serializer.SerializeMessage(envelope, buffer);

    //        var sends = Parallel.ForEachAsync(connections, parallelOptions,
    //            (conn, ct) => conn.Socket.SendAsync(buffer.WrittenMemory, serializer.Type, true, ct));

    //        var withCleanup = RunWithCleanup(sends, objectPool, buffer);
    //        tasks.Add(withCleanup);
    //    }

    //    await Task.WhenAll(tasks);
    //}

    //public async ValueTask SendToGroup(IReadOnlyList<string> groups, WsMessage envelope, SendOptions options = default, CancellationToken ct = default)
    //{
    //    var requested = new HashSet<string>(groups);

    //    var grouped = _groups
    //        .Where(c => requested.Contains(c.Key))
    //        .SelectMany(c => c.Value.Select(c => c.Value))
    //        .GroupBy(c => c.Serializer);

    //    var tasks = new List<Task>();

    //    var parallelOptions = new ParallelOptions
    //    {
    //        MaxDegreeOfParallelism = Environment.ProcessorCount * 10,
    //        CancellationToken = ct
    //    };

    //    foreach (var connections in grouped)
    //    {
    //        var serializer = connections.Key;
    //        var buffer = objectPool.Get();
    //        serializer.SerializeMessage(envelope, buffer);

    //        var sends = Parallel.ForEachAsync(connections, parallelOptions,
    //            (conn, ct) => conn.Socket.SendAsync(buffer.WrittenMemory, serializer.Type, true, ct));

    //        var withCleanup = RunWithCleanup(sends, objectPool, buffer);
    //        tasks.Add(withCleanup);
    //    }

    //    await Task.WhenAll(tasks);
    //}

    public ValueTask Send(WsTarget target, WsMessage envelope, CancellationToken token)
    {
        throw new NotImplementedException();

        static async Task RunWithCleanup(Task work, ObjectPool<ArrayBufferWriter<byte>> objectPool, ArrayBufferWriter<byte> buffer)
        {
            try { await work; }
            finally { objectPool.Return(buffer); }
        }

        //var requested = new HashSet<string>(destination.Connections ?? []);

        //foreach (var group in destination.Groups ?? [])
        //{
        //    if (_groups.TryGetValue(group, out var connections))
        //        requested.RemoveWhere(connections.ContainsKey);
        //}

        //var excludeRequested = new HashSet<string>(destination.Exclude ?? []);
        //requested.RemoveWhere(excludeRequested.Contains);

        //var grouped = _store.Values
        //    .Where(c => requested.Contains(c.Id))
        //    .GroupBy(c => c.Serializer);

        //var tasks = new List<Task>();

        //var options = new ParallelOptions
        //{
        //    MaxDegreeOfParallelism = Environment.ProcessorCount * 10,
        //    CancellationToken = token
        //};

        //foreach (var connections in grouped)
        //{
        //    var serializer = connections.Key;
        //    var buffer = objectPool.Get();
        //    serializer.SerializeMessage(envelope, buffer);

        //    var sends = Parallel.ForEachAsync(connections, options,
        //        (conn, ct) => conn.Socket.SendAsync(buffer.WrittenMemory, serializer.Type, true, ct));

        //    var withCleanup = RunWithCleanup(sends, objectPool, buffer);
        //    tasks.Add(withCleanup);
        //}

        //await Task.WhenAll(tasks);
    }

    public ValueTask Send(WsTargets targets, WsMessage envelope, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}

using Microsoft.Extensions.ObjectPool;
using Moq;
using System.Buffers;
using System.Net.WebSockets;
using WebSockets.Otp.Abstractions.Connections;
using WebSockets.Otp.Abstractions.Options;
using WebSockets.Otp.Abstractions.Serializers;
using WebSockets.Otp.Core.Services;

namespace WebSockets.Otp.Core.Tests;

public sealed class InMemoryConnectionManagerTests
{
    private static WsMessage NewMessage() => new Mock<WsMessage>().Object;

    private static (InMemoryConnectionManager Manager, Mock<ObjectPool<ArrayBufferWriter<byte>>> Pool)NewManager()
    {
        var pool = new Mock<ObjectPool<ArrayBufferWriter<byte>>>();
        pool.Setup(p => p.Get()).Returns(() => new ArrayBufferWriter<byte>());
        pool.Setup(p => p.Return(It.IsAny<ArrayBufferWriter<byte>>()));
        return (new InMemoryConnectionManager(pool.Object), pool);
    }

    private static Mock<IMessageSerializer> NewSerializer(
        byte[]? payload = null,
        WebSocketMessageType type = WebSocketMessageType.Text,
        string protocol = "json")
    {
        payload ??= [0xAA, 0xBB];
        var s = new Mock<IMessageSerializer>();
        s.SetupGet(x => x.Protocol).Returns(protocol);
        s.SetupGet(x => x.Type).Returns(type);
        s.Setup(x => x.SerializeMessage(
                It.IsAny<WsMessage>(),
                It.IsAny<IBufferWriter<byte>>()))
         .Callback<WsMessage, IBufferWriter<byte>>((_, buf) => buf.Write(payload));
        return s;
    }

    private sealed class ConnectionFixture
    {
        public string Id { get; init; } = string.Empty;
        public Mock<IWsConnection> Connection { get; init; } = null!;
        public Mock<WebSocket> Socket { get; init; } = null!;
        public List<byte[]> Sent { get; init; } = new();
        public IWsConnection Object => Connection.Object;
    }

    private static ConnectionFixture NewConnection(string id, IMessageSerializer serializer)
    {
        var sent = new List<byte[]>();
        var socket = new Mock<WebSocket>();
        socket.Setup(s => s.SendAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<WebSocketMessageType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
              .Callback<ReadOnlyMemory<byte>, WebSocketMessageType, bool, CancellationToken>(
                  (data, _, _, _) => sent.Add(data.ToArray()))
              .Returns(ValueTask.CompletedTask);

        var conn = new Mock<IWsConnection>();
        conn.SetupGet(c => c.Id).Returns(id);
        conn.SetupGet(c => c.Socket).Returns(socket.Object);
        conn.SetupGet(c => c.Serializer).Returns(serializer);

        return new ConnectionFixture
        {
            Id = id,
            Connection = conn,
            Socket = socket,
            Sent = sent
        };
    }

    [Fact]
    public async Task TryAdd_NewConnection_ReturnsTrue()
    {
        var (mgr, _) = NewManager();
        var c = NewConnection("c1", NewSerializer().Object);

        Assert.True(await mgr.TryAdd(c.Object, CancellationToken.None));
    }

    [Fact]
    public async Task TryAdd_DuplicateId_ReturnsFalse()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer();
        var c1 = NewConnection("c1", s.Object);
        var c2 = NewConnection("c1", s.Object);

        Assert.True(await mgr.TryAdd(c1.Object, CancellationToken.None));
        Assert.False(await mgr.TryAdd(c2.Object, CancellationToken.None));
    }

    [Fact]
    public async Task TryAdd_IsThreadSafe()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer();
        var tasks = Enumerable.Range(0, 100)
            .Select(i => mgr.TryAdd(NewConnection($"c{i}", s.Object).Object, CancellationToken.None).AsTask());

        var results = await Task.WhenAll(tasks);

        Assert.All(results, Assert.True);
    }

    [Fact]
    public async Task TryRemove_Existing_ReturnsTrue()
    {
        var (mgr, _) = NewManager();
        var c = NewConnection("c1", NewSerializer().Object);
        await mgr.TryAdd(c.Object, CancellationToken.None);

        Assert.True(await mgr.TryRemove("c1", CancellationToken.None));
    }

    [Fact]
    public async Task TryRemove_Unknown_ReturnsFalse()
    {
        var (mgr, _) = NewManager();

        Assert.False(await mgr.TryRemove("nope", CancellationToken.None));
    }

    [Fact]
    public async Task AddToGroup_AddsAndReturnsTrue()
    {
        var (mgr, _) = NewManager();
        var c = NewConnection("c1", NewSerializer().Object);
        await mgr.TryAdd(c.Object, CancellationToken.None);

        Assert.True(await mgr.AddToGroup("g1", "c1", CancellationToken.None));
    }

    [Fact]
    public async Task AddToGroup_Duplicate_ReturnsFalse()
    {
        var (mgr, _) = NewManager();
        var c = NewConnection("c1", NewSerializer().Object);
        await mgr.TryAdd(c.Object, CancellationToken.None);
        await mgr.AddToGroup("g1", "c1", CancellationToken.None);

        Assert.False(await mgr.AddToGroup("g1", "c1", CancellationToken.None));
    }

    [Fact]
    public async Task AddToGroup_UnknownConnection_ThrowsKeyNotFound()
    {
        var (mgr, _) = NewManager();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await mgr.AddToGroup("g1", "missing", CancellationToken.None));
    }

    [Fact]
    public async Task RemoveFromGroup_Existing_ReturnsTrue()
    {
        var (mgr, _) = NewManager();
        var c = NewConnection("c1", NewSerializer().Object);
        await mgr.TryAdd(c.Object, CancellationToken.None);
        await mgr.AddToGroup("g1", "c1", CancellationToken.None);

        Assert.True(await mgr.RemoveFromGroup("g1", "c1", CancellationToken.None));
    }

    [Fact]
    public async Task RemoveFromGroup_Unknown_ReturnsFalse()
    {
        var (mgr, _) = NewManager();

        Assert.False(await mgr.RemoveFromGroup("g1", "c1", CancellationToken.None));
    }

    [Fact]
    public async Task Send_ToAll_SendsToEveryConnection()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer([1, 2]);
        var c1 = NewConnection("c1", s.Object);
        var c2 = NewConnection("c2", s.Object);
        await mgr.TryAdd(c1.Object, CancellationToken.None);
        await mgr.TryAdd(c2.Object, CancellationToken.None);

        await mgr.Send(NewMessage(), CancellationToken.None);

        Assert.Single(c1.Sent);
        Assert.Single(c2.Sent);
        Assert.Equal(new byte[] { 1, 2 }, c1.Sent[0]);
        Assert.Equal(new byte[] { 1, 2 }, c2.Sent[0]);
    }

    [Fact]
    public async Task Send_ToAll_SerializesOncePerSerializerGroup()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer();
        await mgr.TryAdd(NewConnection("c1", s.Object).Object, CancellationToken.None);
        await mgr.TryAdd(NewConnection("c2", s.Object).Object, CancellationToken.None);
        await mgr.TryAdd(NewConnection("c3", s.Object).Object, CancellationToken.None);

        await mgr.Send(NewMessage(), CancellationToken.None);

        s.Verify(x => x.SerializeMessage(
            It.IsAny<WsMessage>(), It.IsAny<IBufferWriter<byte>>()),
            Times.Once);
    }

    [Fact]
    public async Task Send_ToAll_RespectsSerializerType()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer(type: WebSocketMessageType.Binary);
        var c = NewConnection("c1", s.Object);
        await mgr.TryAdd(c.Object, CancellationToken.None);

        await mgr.Send(NewMessage(), CancellationToken.None);

        c.Socket.Verify(x => x.SendAsync(
            It.IsAny<ReadOnlyMemory<byte>>(),
            WebSocketMessageType.Binary,
            true,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Send_ToAll_ReturnsBufferToPool()
    {
        var (mgr, pool) = NewManager();
        var s = NewSerializer();
        await mgr.TryAdd(NewConnection("c1", s.Object).Object, CancellationToken.None);

        await mgr.Send(NewMessage(), CancellationToken.None);

        pool.Verify(p => p.Return(It.IsAny<ArrayBufferWriter<byte>>()), Times.Once);
    }

    [Fact]
    public async Task Send_ToAll_ReturnsBuffer_EvenIfSendThrows()
    {
        var (mgr, pool) = NewManager();
        var s = NewSerializer();

        var socket = new Mock<WebSocket>();
        socket.Setup(x => x.SendAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<WebSocketMessageType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
              .Returns(new ValueTask(Task.FromException(new InvalidOperationException("boom"))));

        var conn = new Mock<IWsConnection>();
        conn.SetupGet(c => c.Id).Returns("c1");
        conn.SetupGet(c => c.Socket).Returns(socket.Object);
        conn.SetupGet(c => c.Serializer).Returns(s.Object);
        await mgr.TryAdd(conn.Object, CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await mgr.Send(NewMessage(), CancellationToken.None));

        pool.Verify(p => p.Return(It.IsAny<ArrayBufferWriter<byte>>()), Times.Once);
    }

    [Fact]
    public async Task Send_ToAll_ManyConnections_AllReceive()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer();
        var conns = Enumerable.Range(0, 50)
            .Select(i => NewConnection($"c{i}", s.Object))
            .ToList();
        foreach (var c in conns) await mgr.TryAdd(c.Object, CancellationToken.None);

        await mgr.Send(NewMessage(), CancellationToken.None);

        Assert.All(conns, c => Assert.Single(c.Sent));
    }

    [Fact]
    public async Task Send_ById_SendsOnlyToThatConnection()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer();
        var c1 = NewConnection("c1", s.Object);
        var c2 = NewConnection("c2", s.Object);
        await mgr.TryAdd(c1.Object, CancellationToken.None);
        await mgr.TryAdd(c2.Object, CancellationToken.None);

        await mgr.Send("c1", NewMessage(), CancellationToken.None);

        Assert.Single(c1.Sent);
        Assert.Empty(c2.Sent);
    }

    [Fact]
    public async Task Send_ById_Unknown_ThrowsKeyNotFound()
    {
        var (mgr, _) = NewManager();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await mgr.Send("missing", NewMessage(), CancellationToken.None));
    }

    [Fact]
    public async Task Send_ByIds_SendsOnlyToRequested()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer();
        var c1 = NewConnection("c1", s.Object);
        var c2 = NewConnection("c2", s.Object);
        var c3 = NewConnection("c3", s.Object);
        await mgr.TryAdd(c1.Object, CancellationToken.None);
        await mgr.TryAdd(c2.Object, CancellationToken.None);
        await mgr.TryAdd(c3.Object, CancellationToken.None);

        await mgr.Send(["c1", "c3"], NewMessage(), CancellationToken.None);

        Assert.Single(c1.Sent);
        Assert.Empty(c2.Sent);
        Assert.Single(c3.Sent);
    }

    [Fact]
    public async Task Send_ByIds_DeduplicatesRepeatedIds()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer();
        var c1 = NewConnection("c1", s.Object);
        await mgr.TryAdd(c1.Object, CancellationToken.None);

        await mgr.Send(["c1", "c1", "c1"], NewMessage(), CancellationToken.None);

        Assert.Single(c1.Sent);
    }

    [Fact]
    public async Task Send_ByIds_UnknownIds_AreIgnored()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer();
        var c1 = NewConnection("c1", s.Object);
        await mgr.TryAdd(c1.Object, CancellationToken.None);

        await mgr.Send(["c1", "missing"], NewMessage(), CancellationToken.None);

        Assert.Single(c1.Sent);
    }

    [Fact]
    public async Task SendToGroup_SendsToAllGroupMembers()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer();
        var c1 = NewConnection("c1", s.Object);
        var c2 = NewConnection("c2", s.Object);
        var c3 = NewConnection("c3", s.Object);
        await mgr.TryAdd(c1.Object, CancellationToken.None);
        await mgr.TryAdd(c2.Object, CancellationToken.None);
        await mgr.TryAdd(c3.Object, CancellationToken.None);
        await mgr.AddToGroup("g1", "c1", CancellationToken.None);
        await mgr.AddToGroup("g1", "c2", CancellationToken.None);

        await mgr.SendToGroup("g1", NewMessage(), CancellationToken.None);

        Assert.Single(c1.Sent);
        Assert.Single(c2.Sent);
        Assert.Empty(c3.Sent);
    }

    [Fact]
    public async Task SendToGroup_UnknownGroup_ThrowsKeyNotFound()
    {
        var (mgr, _) = NewManager();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await mgr.SendToGroup("nope", NewMessage(), CancellationToken.None));
    }

    [Fact]
    public async Task SendToGroups_SendsToUnionOfGroups()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer();
        var c1 = NewConnection("c1", s.Object);
        var c2 = NewConnection("c2", s.Object);
        var c3 = NewConnection("c3", s.Object);
        var c4 = NewConnection("c4", s.Object);
        foreach (var c in new[] { c1, c2, c3, c4 })
            await mgr.TryAdd(c.Object, CancellationToken.None);
        await mgr.AddToGroup("g1", "c1", CancellationToken.None);
        await mgr.AddToGroup("g2", "c2", CancellationToken.None);
        await mgr.AddToGroup("g2", "c3", CancellationToken.None);

        await mgr.SendToGroup(["g1", "g2"], NewMessage(), CancellationToken.None);

        Assert.Single(c1.Sent);
        Assert.Single(c2.Sent);
        Assert.Single(c3.Sent);
        Assert.Empty(c4.Sent);
    }

    [Fact]
    public async Task SendToGroups_UnknownGroups_AreIgnored()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer();
        var c1 = NewConnection("c1", s.Object);
        await mgr.TryAdd(c1.Object, CancellationToken.None);
        await mgr.AddToGroup("g1", "c1", CancellationToken.None);

        await mgr.SendToGroup(["g1", "missing"], NewMessage(), CancellationToken.None);

        Assert.Single(c1.Sent);
    }

    [Fact]
    public async Task SendToGroups_NoMatches_Completes()
    {
        var (mgr, _) = NewManager();

        await mgr.SendToGroup(["nope"], NewMessage(), CancellationToken.None);
    }

    [Fact]
    public async Task SendToGroups_ConnectionInMultipleGroups_ReceivesMessageOnce()
    {
        var (mgr, _) = NewManager();
        var s = NewSerializer();
        var c1 = NewConnection("c1", s.Object);
        await mgr.TryAdd(c1.Object, CancellationToken.None);
        await mgr.AddToGroup("g1", "c1", CancellationToken.None);
        await mgr.AddToGroup("g2", "c1", CancellationToken.None);

        await mgr.SendToGroup(["g1", "g2"], NewMessage(), CancellationToken.None);

        Assert.Equal(2, c1.Sent.Count);
    }

    [Fact]
    public async Task Send_ById_ReturnsBuffer_EvenIfSendThrows()
    {
        var (mgr, pool) = NewManager();
        var s = NewSerializer();

        var socket = new Mock<WebSocket>();
        socket.Setup(x => x.SendAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<WebSocketMessageType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
              .Returns(new ValueTask(Task.FromException(new InvalidOperationException("boom"))));

        var conn = new Mock<IWsConnection>();
        conn.SetupGet(c => c.Id).Returns("c1");
        conn.SetupGet(c => c.Socket).Returns(socket.Object);
        conn.SetupGet(c => c.Serializer).Returns(s.Object);
        await mgr.TryAdd(conn.Object, CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await mgr.Send("c1", NewMessage(), CancellationToken.None));

        pool.Verify(p => p.Return(It.IsAny<ArrayBufferWriter<byte>>()), Times.Once);
    }
}

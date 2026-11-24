using Moq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WebSockets.Otp.Abstractions.Options;
using WebSockets.Otp.Abstractions.Serializers;
using WebSockets.Otp.Abstractions.Transport;
using WebSockets.Otp.Core.Services;

namespace WebSockets.Otp.Core.Tests;

public sealed class DefaultHandshakeHandlerTests
{
    private sealed class Mocks
    {
        public Mock<ISerializerStore> Store { get; } = new();
        public Mock<IMessageEnumerator> Enumerator { get; } = new();
    }

    private static DefaultHandshakeHandler CreateHandler(Mocks m) =>
        new(m.Store.Object, m.Enumerator.Object);

    private static WsOptions DefaultOptions() => new(new WsConfig());

    private static Mock<IMessageBuffer> CreateBuffer(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var buffer = new Mock<IMessageBuffer>();
        buffer.SetupGet(b => b.Memory).Returns(new Memory<byte>(bytes));
        return buffer;
    }

    private static async IAsyncEnumerable<T> OneItem<T>(T item)
    {
        await Task.Yield();
        yield return item;
    }

    private static async IAsyncEnumerable<T> NoItems<T>()
    {
        await Task.Yield();
        yield break;
    }

    private static async IAsyncEnumerable<IMessageBuffer> FaultedEnumerable()
    {
        await Task.Yield();
        await Task.FromException(new InvalidOperationException("boom"));
        yield break;
    }

    private static Mock<WebSocket> CreateSocket()
    {
        var socket = new Mock<WebSocket>(MockBehavior.Loose);
        socket
            .Setup(s => s.SendAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<WebSocketMessageType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        return socket;
    }

    private static Mock<IMessageEnumerator> StubEnumerator(Mocks mocks, IAsyncEnumerable<IMessageBuffer> source)
    {
        mocks.Enumerator
            .Setup(e => e.EnumerateAsync(It.IsAny<WebSocket>(), It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()))
            .Returns(source);
        return mocks.Enumerator;
    }

    [Fact]
    public async Task HandleAsync_NoMessages_ReturnsError_AndDoesNotSendOrReturnBuffer()
    {
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);
        StubEnumerator(mocks, NoItems<IMessageBuffer>());

        var socket = CreateSocket();
        var result = await handler.HandleAsync(socket.Object, DefaultOptions(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Handshake failed: no initial message received.", result.Error.Value);
        socket.Verify(
            s => s.SendAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<WebSocketMessageType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        mocks.Enumerator.Verify(
            e => e.ReturnBuffer(It.IsAny<IMessageBuffer>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_JsonNull_ReturnsError_AndReturnsBuffer()
    {
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);
        var buffer = CreateBuffer("null");
        StubEnumerator(mocks, OneItem(buffer.Object));

        var socket = CreateSocket();
        var result = await handler.HandleAsync(socket.Object, DefaultOptions(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Handshake failed: could not deserialize handshake options.", result.Error.Value);
        socket.Verify(
            s => s.SendAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<WebSocketMessageType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        mocks.Enumerator.Verify(e => e.ReturnBuffer(buffer.Object), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_MalformedJson_Throws_AndReturnsBufferInFinally()
    {
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);
        var buffer = CreateBuffer("this is definitely not json");
        StubEnumerator(mocks, OneItem(buffer.Object));

        var socket = CreateSocket();

        await Assert.ThrowsAsync<JsonException>(
            async () => await handler.HandleAsync(socket.Object, DefaultOptions(), CancellationToken.None));

        mocks.Enumerator.Verify(e => e.ReturnBuffer(buffer.Object), Times.Once);
        socket.Verify(
            s => s.SendAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<WebSocketMessageType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_UnsupportedProtocol_ReturnsError_AndReturnsBuffer()
    {
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);
        var buffer = CreateBuffer("""{"protocol":"unknown"}""");
        StubEnumerator(mocks, OneItem(buffer.Object));

        IMessageSerializer? unused = null;
        mocks.Store.Setup(s => s.TryGet("unknown", out unused)).Returns(false);

        var socket = CreateSocket();
        var result = await handler.HandleAsync(socket.Object, DefaultOptions(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal($"Handshake failed: unsupported transfer protocol 'unknown'.", result.Error.Value);
        socket.Verify(
            s => s.SendAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<WebSocketMessageType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        mocks.Enumerator.Verify(e => e.ReturnBuffer(buffer.Object), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenEnumeratorFaults_Propagates()
    {
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);
        StubEnumerator(mocks, FaultedEnumerable());

        var socket = CreateSocket();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await handler.HandleAsync(socket.Object, DefaultOptions(), CancellationToken.None));

        mocks.Enumerator.Verify(
            e => e.ReturnBuffer(It.IsAny<IMessageBuffer>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidHandshake_SendsEmptyJsonObjectResponse()
    {
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);
        var buffer = CreateBuffer("""{"protocol":"json"}""");
        StubEnumerator(mocks, OneItem<IMessageBuffer>(buffer.Object));

        IMessageSerializer? serializer = Mock.Of<IMessageSerializer>();
        mocks.Store.Setup(s => s.TryGet("json", out serializer)).Returns(true);

        var socket = CreateSocket();
        var result = await handler.HandleAsync(socket.Object, DefaultOptions(), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(result.Value);
        Assert.Equal("json", result.Value.Protocol);

        socket.Verify(
            s => s.SendAsync(
                It.Is<ReadOnlyMemory<byte>>(m => m.ToArray().SequenceEqual(Encoding.UTF8.GetBytes("{}"))),
                WebSocketMessageType.Text,
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);

        mocks.Enumerator.Verify(e => e.ReturnBuffer(buffer.Object), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ValidHandshake_DeserializesCamelCaseJsonCaseInsensitively()
    {
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);

        var buffer = CreateBuffer("""{"Protocol":"json"}""");
        StubEnumerator(mocks, OneItem(buffer.Object));

        IMessageSerializer? serializer = Mock.Of<IMessageSerializer>();
        mocks.Store.Setup(s => s.TryGet("json", out serializer)).Returns(true);

        var socket = CreateSocket();
        var result = await handler.HandleAsync(socket.Object, DefaultOptions(), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("json", result.Value.Protocol);
    }

    [Fact]
    public async Task HandleAsync_ValidHandshake_PassesCancellationTokenToEnumeratorAndSendAsync()
    {
        var cts = new CancellationTokenSource();
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);
        var buffer = CreateBuffer("""{"protocol":"json"}""");

        CancellationToken enumeratorToken = default;
        mocks.Enumerator
            .Setup(e => e.EnumerateAsync(It.IsAny<WebSocket>(), It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()))
            .Callback<WebSocket, WsOptions, CancellationToken>((_, _, t) => enumeratorToken = t)
            .Returns(OneItem(buffer.Object));

        IMessageSerializer? serializer = Mock.Of<IMessageSerializer>();
        mocks.Store.Setup(s => s.TryGet("json", out serializer)).Returns(true);

        var socket = CreateSocket();
        await handler.HandleAsync(socket.Object, DefaultOptions(), cts.Token);

        Assert.Equal(cts.Token, enumeratorToken);
        socket.Verify(
            s => s.SendAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<WebSocketMessageType>(),
                It.IsAny<bool>(),
                It.Is<CancellationToken>(t => t == cts.Token)),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenSendThrows_StillReturnsBuffer()
    {
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);
        var buffer = CreateBuffer("""{"protocol":"json"}""");
        StubEnumerator(mocks, OneItem(buffer.Object));

        IMessageSerializer? serializer = Mock.Of<IMessageSerializer>();
        mocks.Store.Setup(s => s.TryGet("json", out serializer)).Returns(true);

        var socket = new Mock<WebSocket>();
        socket
            .Setup(s => s.SendAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<WebSocketMessageType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Throws(new WebSocketException("socket closed"));

        await Assert.ThrowsAsync<WebSocketException>(
            async () => await handler.HandleAsync(socket.Object, DefaultOptions(), CancellationToken.None));

        mocks.Enumerator.Verify(e => e.ReturnBuffer(buffer.Object), Times.Once);
    }
}

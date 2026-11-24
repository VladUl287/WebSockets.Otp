using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.WebSockets;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Abstractions.Middleware;
using WebSockets.Otp.Abstractions.Options;
using WebSockets.Otp.Abstractions.Serializers;
using WebSockets.Otp.Abstractions.Transport;
using WebSockets.Otp.Abstractions.Utils;
using WebSockets.Otp.Core.Services;

namespace WebSockets.Otp.Core.Tests;

public sealed class DefaultConnectionHandlerTests
{
    private sealed class Mocks
    {
        public Mock<IWsConnectionManager> ConnectionManager { get; } = new();
        public Mock<IWsConnectionFactory> ConnectionFactory { get; } = new();
        public Mock<IHandshakeHandler> HandshakeService { get; } = new();
        public Mock<IContextFactory> ContextFactory { get; } = new();
        public Mock<IMessageProcessor> MessageProcessor { get; } = new();
        public Mock<ISerializerStore> SerializerStore { get; } = new();
        public Mock<ILogger<DefaultConnectionHandler>> Logger { get; } = new();
    }

    private static DefaultConnectionHandler CreateHandler(Mocks m) => new(
        m.ConnectionManager.Object,
        m.ConnectionFactory.Object,
        m.HandshakeService.Object,
        m.ContextFactory.Object,
        m.MessageProcessor.Object,
        m.SerializerStore.Object,
        m.Logger.Object);

    private static (DefaultHttpContext Context, Mock<WebSocket> Socket) CreateWebSocketContext(
        CancellationToken token = default)
    {
        var socket = new Mock<WebSocket>(MockBehavior.Loose);
        socket
            .Setup(s => s.CloseOutputAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        socket
            .Setup(s => s.CloseAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var feature = new Mock<IHttpWebSocketFeature>();
        feature.SetupGet(f => f.IsWebSocketRequest).Returns(true);
        feature
            .Setup(f => f.AcceptAsync(It.IsAny<WebSocketAcceptContext>()))
            .ReturnsAsync(socket.Object);

        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(feature.Object);
        context.RequestAborted = token;
        return (context, socket);
    }

    private static Result<WsHandshakeOptions, Error<string>> HandshakeOk(string protocol)
        => Result<WsHandshakeOptions, Error<string>>.Success(new WsHandshakeOptions(protocol));

    private static Result<WsHandshakeOptions, Error<string>> HandshakeFail(string result = null)
    {
        Result<WsHandshakeOptions, Error<string>> failed = new Error<string>(result);
        return failed;
    }

    private static WsOptions DefaultOptions() => new(new WsConfig());

    [Fact]
    public async Task HandleAsync_WhenTokenAlreadyCancelled_ReturnsWithoutAcceptingSocket()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var (context, _) = CreateWebSocketContext(cts.Token);
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);

        await handler.HandleAsync(context, DefaultOptions());

        mocks.HandshakeService.Verify(
            h => h.HandleAsync(It.IsAny<WebSocket>(), It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
        mocks.ConnectionManager.Verify(
            m => m.TryAdd(It.IsAny<IWsConnection>(), It.IsAny<CancellationToken>()),
            Times.Never);
        mocks.MessageProcessor.Verify(
            p => p.Process(It.IsAny<IGlobalContext>(), It.IsAny<IMessageSerializer>(), It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenHandshakeReturnsError_DoesNotAddConnection()
    {
        var (context, _) = CreateWebSocketContext();
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);

        mocks.HandshakeService
            .Setup(h => h.HandleAsync(It.IsAny<WebSocket>(), It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandshakeFail());

        await handler.HandleAsync(context, DefaultOptions());

        mocks.ConnectionManager.Verify(
            m => m.TryAdd(It.IsAny<IWsConnection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenHandshakeThrows_DoesNotAddConnection()
    {
        var (context, _) = CreateWebSocketContext();
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);

        mocks.HandshakeService
            .Setup(h => h.HandleAsync(It.IsAny<WebSocket>(), It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await handler.HandleAsync(context, DefaultOptions());

        mocks.ConnectionManager.Verify(
            m => m.TryAdd(It.IsAny<IWsConnection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenHandshakeCancelled_DoesNotAddConnection()
    {
        var cts = new CancellationTokenSource();
        var (context, _) = CreateWebSocketContext(cts.Token);
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);

        mocks.HandshakeService
            .Setup(h => h.HandleAsync(It.IsAny<WebSocket>(), It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException());

        await handler.HandleAsync(context, DefaultOptions());

        mocks.ConnectionManager.Verify(
            m => m.TryAdd(It.IsAny<IWsConnection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenSerializerNotFound_DoesNotAddConnection()
    {
        var (context, _) = CreateWebSocketContext();
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);

        mocks.HandshakeService
            .Setup(h => h.HandleAsync(It.IsAny<WebSocket>(), It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandshakeOk("unknown"));

        IMessageSerializer? unused = null;
        mocks.SerializerStore
            .Setup(s => s.TryGet("unknown", out unused))
            .Returns(false);

        await handler.HandleAsync(context, DefaultOptions());

        mocks.ConnectionManager.Verify(
            m => m.TryAdd(It.IsAny<IWsConnection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenTryAddReturnsFalse_DoesNotRunSessionOrRemoveConnection()
    {
        var (context, _) = CreateWebSocketContext();
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);

        var serializer = new Mock<IMessageSerializer>().Object;
        var connection = new Mock<IWsConnection>();
        connection.SetupGet(c => c.Id).Returns(Guid.NewGuid().ToString());

        mocks.HandshakeService
            .Setup(h => h.HandleAsync(It.IsAny<WebSocket>(), It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandshakeOk("json"));
        mocks.SerializerStore
            .Setup(s => s.TryGet("json", out serializer))
            .Returns(true);
        mocks.ConnectionFactory
            .Setup(f => f.Create(It.IsAny<WebSocket>(), serializer))
            .Returns(connection.Object);
        mocks.ConnectionManager
            .Setup(m => m.TryAdd(connection.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await handler.HandleAsync(context, DefaultOptions());

        mocks.ContextFactory.Verify(
            c => c.CreateGlobal(It.IsAny<IWsConnection>(), It.IsAny<HttpContext>(), It.IsAny<WebSocket>()),
            Times.Never);
        mocks.MessageProcessor.Verify(
            p => p.Process(It.IsAny<IGlobalContext>(), It.IsAny<IMessageSerializer>(), It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
        mocks.ConnectionManager.Verify(
            m => m.TryRemove(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_AddsProcessesRemovesAndInvokesCallbacks()
    {
        var (context, _) = CreateWebSocketContext();
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);

        var onConnected = 0;
        var onDisconnected = 0;
        var options = new WsOptions(new WsConfig
        {
            OnConnected = _ => { onConnected++; return Task.CompletedTask; },
            OnDisconnected = _ => { onDisconnected++; return Task.CompletedTask; },
        });

        var serializer = new Mock<IMessageSerializer>().Object;
        var connection = new Mock<IWsConnection>();
        var connectionId = Guid.NewGuid().ToString();
        connection.SetupGet(c => c.Id).Returns(connectionId);
        var globalContext = new Mock<IGlobalContext>().Object;

        mocks.HandshakeService
            .Setup(h => h.HandleAsync(It.IsAny<WebSocket>(), It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandshakeOk("json"));
        mocks.SerializerStore
            .Setup(s => s.TryGet("json", out serializer))
            .Returns(true);
        mocks.ConnectionFactory
            .Setup(f => f.Create(It.IsAny<WebSocket>(), serializer))
            .Returns(connection.Object);
        mocks.ConnectionManager
            .Setup(m => m.TryAdd(connection.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mocks.ConnectionManager
            .Setup(m => m.TryRemove(connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mocks.ContextFactory
            .Setup(c => c.CreateGlobal(connection.Object, It.IsAny<HttpContext>(), It.IsAny<WebSocket>()))
            .Returns(globalContext);
        mocks.MessageProcessor
            .Setup(p => p.Process(globalContext, serializer, options, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await handler.HandleAsync(context, options);

        mocks.ConnectionManager.Verify(m => m.TryAdd(connection.Object, It.IsAny<CancellationToken>()), Times.Once);
        mocks.MessageProcessor.Verify(
            p => p.Process(globalContext, serializer, options, It.IsAny<CancellationToken>()),
            Times.Once);
        mocks.ConnectionManager.Verify(m => m.TryRemove(connectionId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, onConnected);
        Assert.Equal(1, onDisconnected);
    }

    [Fact]
    public async Task HandleAsync_PassesRequestAbortedTokenToMessageProcessor()
    {
        var cts = new CancellationTokenSource();
        var (context, _) = CreateWebSocketContext(cts.Token);
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);

        var serializer = new Mock<IMessageSerializer>().Object;
        var connection = new Mock<IWsConnection>();
        connection.SetupGet(c => c.Id).Returns(Guid.NewGuid().ToString());
        var globalContext = new Mock<IGlobalContext>().Object;

        mocks.HandshakeService
            .Setup(h => h.HandleAsync(It.IsAny<WebSocket>(), It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandshakeOk("json"));
        mocks.SerializerStore
            .Setup(s => s.TryGet("json", out serializer))
            .Returns(true);
        mocks.ConnectionFactory
            .Setup(f => f.Create(It.IsAny<WebSocket>(), serializer))
            .Returns(connection.Object);
        mocks.ConnectionManager
            .Setup(m => m.TryAdd(It.IsAny<IWsConnection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mocks.ContextFactory
            .Setup(c => c.CreateGlobal(It.IsAny<IWsConnection>(), It.IsAny<HttpContext>(), It.IsAny<WebSocket>()))
            .Returns(globalContext);

        CancellationToken observed = default;
        mocks.MessageProcessor
            .Setup(p => p.Process(It.IsAny<IGlobalContext>(), serializer, It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IGlobalContext, IMessageSerializer, WsOptions, CancellationToken>(
                (_, _, _, t) => observed = t)
            .Returns(Task.CompletedTask);

        await handler.HandleAsync(context, DefaultOptions());

        Assert.Equal(cts.Token, observed);
    }

    [Fact]
    public async Task HandleAsync_WhenMessageProcessorThrows_StillRemovesConnectionAndRunsOnDisconnected()
    {
        var (context, _) = CreateWebSocketContext();
        var mocks = new Mocks();
        var handler = CreateHandler(mocks);

        var onConnected = 0;
        var onDisconnected = 0;
        var options = new WsOptions(new WsConfig
        {
            OnConnected = _ => { onConnected++; return Task.CompletedTask; },
            OnDisconnected = _ => { onDisconnected++; return Task.CompletedTask; },
        });

        var serializer = new Mock<IMessageSerializer>().Object;
        var connection = new Mock<IWsConnection>();
        var connectionId = Guid.NewGuid().ToString();
        connection.SetupGet(c => c.Id).Returns(connectionId);
        var globalContext = new Mock<IGlobalContext>().Object;

        mocks.HandshakeService
            .Setup(h => h.HandleAsync(It.IsAny<WebSocket>(), It.IsAny<WsOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandshakeOk("json"));
        mocks.SerializerStore
            .Setup(s => s.TryGet("json", out serializer))
            .Returns(true);
        mocks.ConnectionFactory
            .Setup(f => f.Create(It.IsAny<WebSocket>(), serializer))
            .Returns(connection.Object);
        mocks.ConnectionManager
            .Setup(m => m.TryAdd(connection.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mocks.ConnectionManager
            .Setup(m => m.TryRemove(connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mocks.ContextFactory
            .Setup(c => c.CreateGlobal(connection.Object, It.IsAny<HttpContext>(), It.IsAny<WebSocket>()))
            .Returns(globalContext);
        mocks.MessageProcessor
            .Setup(p => p.Process(globalContext, serializer, options, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(context, options));

        Assert.Equal("boom", ex.Message);
        Assert.Equal(1, onConnected);
        Assert.Equal(1, onDisconnected);
        mocks.ConnectionManager.Verify(m => m.TryRemove(connectionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void WsConfig_HasSensibleDefaults()
    {
        var config = new WsConfig();

        Assert.Empty(config.AuthorizationData);
        Assert.NotNull(config.WebSocketOptions);
        Assert.Equal(Environment.ProcessorCount * 10, config.MaxDegreeOfParallelism);
        Assert.Null(config.TaskScheduler);
        Assert.Equal(4 * 1024, config.DefaultBufferSize);
        Assert.Equal(4 * 1024 * 1024, config.MaxBufferSize);
        Assert.True(config.ShrinkBuffer);
        Assert.Null(config.OnConnected);
        Assert.Null(config.OnDisconnected);
    }

    [Fact]
    public void WsOptions_CopiesAllValuesFromConfig()
    {
        var authData = new List<IAuthorizeData> { Mock.Of<IAuthorizeData>() };
        var wsOptions = new WebSocketOptions();
        var scheduler = TaskScheduler.Default;
        Func<IGlobalContext, Task> onConnected = _ => Task.CompletedTask;
        Func<IGlobalContext, Task> onDisconnected = _ => Task.CompletedTask;

        var config = new WsConfig
        {
            AuthorizationData = authData,
            WebSocketOptions = wsOptions,
            MaxDegreeOfParallelism = 42,
            TaskScheduler = scheduler,
            DefaultBufferSize = 8192,
            MaxBufferSize = 1 << 20,
            ShrinkBuffer = false,
            OnConnected = onConnected,
            OnDisconnected = onDisconnected,
        };

        var options = new WsOptions(config);

        Assert.Same(authData, options.AuthorizationData);
        Assert.Same(wsOptions, options.WebSocketOptions);
        Assert.Equal(42, options.MaxDegreeOfParallelism);
        Assert.Same(scheduler, options.TaskScheduler);
        Assert.Equal(8192, options.DefaultBufferSize);
        Assert.Equal(1 << 20, options.MaxBufferSize);
        Assert.False(options.ShrinkBuffer);
        Assert.Same(onConnected, options.OnConnected);
        Assert.Same(onDisconnected, options.OnDisconnected);
    }

    [Fact]
    public void WsOptions_DefaultsMatchWsConfigDefaults()
    {
        var options = new WsOptions(new WsConfig());

        Assert.Equal(Environment.ProcessorCount * 10, options.MaxDegreeOfParallelism);
        Assert.Equal(4 * 1024, options.DefaultBufferSize);
        Assert.Equal(4 * 1024 * 1024, options.MaxBufferSize);
        Assert.True(options.ShrinkBuffer);
        Assert.Null(options.TaskScheduler);
    }
}
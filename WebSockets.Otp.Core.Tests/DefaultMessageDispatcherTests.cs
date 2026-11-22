using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Abstractions.Serializers;
using WebSockets.Otp.Abstractions.Transport;
using WebSockets.Otp.Abstractions.Utils;
using WebSockets.Otp.Core.Services;

namespace WebSockets.Otp.Core.Tests;

public sealed class DefaultMessageDispatcherTests
{
    private sealed class TestEndpoint { }

    private sealed class Fixture
    {
        public Mock<IServiceScopeFactory> ScopeFactory { get; } = new();
        public Mock<IServiceScope> Scope { get; } = new();
        public Mock<IContextFactory> ContextFactory { get; } = new();
        public Mock<ITrieResolver<WsEndpointInfo>> Resolver { get; } = new();
        public Mock<IWsConnectionManager> Manager { get; } = new();
        public Mock<IMessageSerializer> Serializer { get; } = new();
        public Mock<IMessageBuffer> Payload { get; } = new();
        public Mock<IGlobalContext> GlobalContext { get; } = new();
        public Mock<IWsConnection> Connection { get; } = new();
        public Mock<HttpContext> HttpContext { get; } = new();
        public Mock<IEndpointAuthorizer> Authorizer { get; } = new();
        public Mock<IEndpointInvoker> Invoker { get; } = new();
        public Mock<IEndpointContext> EndpointContext { get; } = new();
        public ILogger<DefaultMessageDispatcher> Logger { get; } = NullLogger<DefaultMessageDispatcher>.Instance;

        public ServiceProvider Services { get; private set; } = null!;
        public DefaultMessageDispatcher Dispatcher { get; private set; } = null!;

        public Fixture()
        {
            Payload.SetupGet(p => p.Memory).Returns(new byte[] { 1, 2, 3 });
            GlobalContext.SetupGet(c => c.Connection).Returns(Connection.Object);
            GlobalContext.SetupGet(c => c.Context).Returns(HttpContext.Object);
            Connection.SetupGet(c => c.Id).Returns("conn-1");

            Services = new ServiceCollection()
                .AddSingleton(Authorizer.Object)
                .AddSingleton(new TestEndpoint())
                .BuildServiceProvider();

            Scope.SetupGet(s => s.ServiceProvider).Returns(Services);
            ScopeFactory.Setup(f => f.CreateScope()).Returns(Scope.Object);

            Dispatcher = new DefaultMessageDispatcher(
                ScopeFactory.Object,
                ContextFactory.Object,
                Resolver.Object,
                Manager.Object,
                Logger);
        }

        public static MessageHeaders Headers(uint id, byte[]? key = null, byte[]? value = null)
            => new()
            {
                CorrelationId = id,
                Key = key ?? [0xAB],
                Value = value ?? [0xCD]
            };

        public void GivenHeaders(MessageHeaders headers)
            => Serializer
                .Setup(s => s.ReadHeaders(It.IsAny<ReadOnlyMemory<byte>>()))
                .Returns(headers);

        public WsEndpointInfo GivenEndpoint(bool withEndpoint = true)
        {
            var info = new WsEndpointInfo
            {
                Key = string.Empty,
                Endpoint = withEndpoint ? new(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "test-auth") : null,
                Type = typeof(TestEndpoint),
                Invoker = Invoker.Object
            };

            Resolver
                .Setup(r => r.TryResolve(It.IsAny<ReadOnlyMemory<byte>>(), out It.Ref<WsEndpointInfo?>.IsAny))
                .Returns((ReadOnlyMemory<byte> _, out WsEndpointInfo? value) =>
                {
                    value = info;
                    return true;
                });
            return info;
        }

        public void GivenResolverMiss()
            => Resolver
                .Setup(r => r.TryResolve(
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    out It.Ref<WsEndpointInfo?>.IsAny))
                .Returns((ReadOnlyMemory<byte> _, out WsEndpointInfo? value) =>
                {
                    value = null;
                    return false;
                });

        public void GivenAuthorization(bool success, string error = "denied")
            => Authorizer
                .Setup(a => a.AuthorizeAsync(
                    It.IsAny<HttpContext>(), It.IsAny<WsEndpointInfo>()))
                .ReturnsAsync(success ? SuccessResult() : ErrorResult(error));

        public void GivenContextFactory()
            => ContextFactory
                .Setup(c => c.Create(
                    It.IsAny<uint>(),
                    It.IsAny<IGlobalContext>(),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(EndpointContext.Object);
    }

    private static Result<Success, string> SuccessResult() => new Success();

    private static Result<Success, string> ErrorResult(string error) => error;

    private static CancellationToken None => CancellationToken.None;

    [Fact]
    public async Task DispatchMessage_ReadsHeaders_FromPayloadMemory()
    {
        var f = new Fixture();
        f.GivenHeaders(Fixture.Headers(1));
        f.GivenEndpoint();
        f.GivenAuthorization(true);
        f.GivenContextFactory();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.Serializer.Verify(s => s.ReadHeaders(
            It.Is<ReadOnlyMemory<byte>>(m => m.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }))),
            Times.Once);
    }

    [Fact]
    public async Task DispatchMessage_EmptyKey_ShortCircuits_NoScope_NoInvoke()
    {
        var f = new Fixture();
        f.GivenHeaders(Fixture.Headers(1, key: Array.Empty<byte>()));

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.Resolver.Verify(r => r.TryResolve(
            It.IsAny<ReadOnlyMemory<byte>>(),
            out It.Ref<WsEndpointInfo?>.IsAny), Times.Never);
        f.ScopeFactory.Verify(s => s.CreateScope(), Times.Never);
        f.Invoker.Verify(i => i.Invoke(
            It.IsAny<object>(), It.IsAny<IEndpointContext>()), Times.Never);
    }

    [Fact]
    public async Task DispatchMessage_ResolverMiss_ShortCircuits_NoScope_NoInvoke()
    {
        var f = new Fixture();
        f.GivenHeaders(Fixture.Headers(1));
        f.GivenResolverMiss();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.ScopeFactory.Verify(s => s.CreateScope(), Times.Never);
        f.Invoker.Verify(i => i.Invoke(
            It.IsAny<object>(), It.IsAny<IEndpointContext>()), Times.Never);
    }

    [Fact]
    public async Task DispatchMessage_PassesHeaderKey_ToResolver()
    {
        var f = new Fixture();
        var key = new byte[] { 0xAA, 0xBB, 0xCC };
        f.GivenHeaders(Fixture.Headers(1, key: key));
        f.GivenEndpoint();
        f.GivenAuthorization(true);
        f.GivenContextFactory();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.Resolver.Verify(r => r.TryResolve(
            It.Is<ReadOnlyMemory<byte>>(m => m.ToArray().SequenceEqual(key)),
            out It.Ref<WsEndpointInfo?>.IsAny), Times.Once);
    }

    [Fact]
    public async Task DispatchMessage_EndpointNull_SkipsAuthorization_StillInvokes()
    {
        var f = new Fixture();
        f.GivenHeaders(Fixture.Headers(1));
        f.GivenEndpoint(withEndpoint: false);
        f.GivenContextFactory();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.Authorizer.Verify(a => a.AuthorizeAsync(
            It.IsAny<HttpContext>(), It.IsAny<WsEndpointInfo>()), Times.Never);
        f.Invoker.Verify(i => i.Invoke(
            It.IsAny<object>(), f.EndpointContext.Object), Times.Once);
    }

    [Fact]
    public async Task DispatchMessage_AuthCalled_WithHttpContextAndResolvedInfo()
    {
        var f = new Fixture();
        var info = f.GivenEndpoint();
        f.GivenHeaders(Fixture.Headers(1));
        f.GivenAuthorization(true);
        f.GivenContextFactory();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.Authorizer.Verify(a => a.AuthorizeAsync(f.HttpContext.Object, info), Times.Once);
    }

    [Fact]
    public async Task DispatchMessage_AuthSuccess_InvokesEndpoint()
    {
        var f = new Fixture();
        f.GivenHeaders(Fixture.Headers(1));
        f.GivenEndpoint();
        f.GivenAuthorization(true);
        f.GivenContextFactory();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.Invoker.Verify(i => i.Invoke(It.IsAny<object>(), f.EndpointContext.Object), Times.Once);
        f.Manager.Verify(m => m.Send(
            It.IsAny<string>(), It.IsAny<MessageHeaders>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchMessage_AuthFailure_DoesNotInvokeEndpoint()
    {
        var f = new Fixture();
        f.GivenHeaders(Fixture.Headers(id: 42));
        f.GivenEndpoint();
        f.GivenAuthorization(false, "no access");
        f.GivenContextFactory();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.Invoker.Verify(i => i.Invoke(
            It.IsAny<object>(), It.IsAny<IEndpointContext>()), Times.Never);
    }

    [Fact]
    public async Task DispatchMessage_AuthFailure_NoCorrelationId_DoesNotSendError()
    {
        var f = new Fixture();
        f.GivenHeaders(Fixture.Headers(id: 0));
        f.GivenEndpoint();
        f.GivenAuthorization(false);
        f.GivenContextFactory();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.Manager.Verify(m => m.Send(
            It.IsAny<string>(), It.IsAny<MessageHeaders>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchMessage_AuthFailure_WithCorrelationId_SendsErrorToConnection()
    {
        var f = new Fixture();
        f.GivenHeaders(Fixture.Headers(id: 42));
        f.GivenEndpoint();
        f.GivenAuthorization(false, "no access");
        f.GivenContextFactory();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.Manager.Verify(m => m.Send(
            "conn-1",
            It.Is<MessageHeaders>(m => m is ErrorMessage),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchMessage_ResolvesEndpoint_FromServices_ByEndpointType()
    {
        var f = new Fixture();
        f.GivenHeaders(Fixture.Headers(1));
        f.GivenEndpoint();
        f.GivenAuthorization(true);
        f.GivenContextFactory();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.Invoker.Verify(i => i.Invoke(
            It.Is<object>(e => e is TestEndpoint),
            It.IsAny<IEndpointContext>()), Times.Once);
    }

    [Fact]
    public async Task DispatchMessage_CreatesEndpointContext_WithCorrectArguments()
    {
        var f = new Fixture();
        var value = new byte[] { 9, 8, 7 };
        f.GivenHeaders(Fixture.Headers(id: 7, value: value));
        f.GivenEndpoint();
        f.GivenAuthorization(true);
        f.GivenContextFactory();

        using var cts = new CancellationTokenSource();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, cts.Token);

        f.ContextFactory.Verify(c => c.Create(
            7u,
            f.GlobalContext.Object,
            It.Is<ReadOnlyMemory<byte>>(m => m.ToArray().SequenceEqual(value)),
            cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task DispatchMessage_PassesEndpointContext_ToInvoker()
    {
        var f = new Fixture();
        f.GivenHeaders(Fixture.Headers(1));
        f.GivenEndpoint();
        f.GivenAuthorization(true);
        f.GivenContextFactory();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.Invoker.Verify(i => i.Invoke(
            It.IsAny<object>(), f.EndpointContext.Object), Times.Once);
    }

    [Fact]
    public async Task DispatchMessage_DisposesScope_OnSuccess()
    {
        var f = new Fixture();
        f.GivenHeaders(Fixture.Headers(1));
        f.GivenEndpoint(withEndpoint: false);
        f.GivenContextFactory();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.Scope.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public async Task DispatchMessage_DisposesScope_WhenAuthFails()
    {
        var f = new Fixture();
        f.GivenHeaders(Fixture.Headers(1));
        f.GivenEndpoint();
        f.GivenAuthorization(false);
        f.GivenContextFactory();

        await f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None);

        f.Scope.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public async Task DispatchMessage_DisposesScope_WhenInvokerThrows()
    {
        var f = new Fixture();
        f.GivenHeaders(Fixture.Headers(1));
        f.GivenEndpoint();
        f.GivenAuthorization(true);
        f.GivenContextFactory();
        f.Invoker
            .Setup(i => i.Invoke(It.IsAny<object>(), It.IsAny<IEndpointContext>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Dispatcher.DispatchMessage(
            f.GlobalContext.Object, f.Serializer.Object, f.Payload.Object, None));

        f.Scope.Verify(s => s.Dispose(), Times.Once);
    }
}
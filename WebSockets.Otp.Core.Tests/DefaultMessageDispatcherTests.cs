using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Abstractions.Serializers;
using WebSockets.Otp.Abstractions.Transport;
using WebSockets.Otp.Abstractions.Utils;
using WebSockets.Otp.Core.Extensions;
using WebSockets.Otp.Core.Services;

namespace WebSockets.Otp.Core.Tests;

public class DefaultMessageDispatcherTests
{
    private sealed class TestEndpoint { }

    private sealed class FakeResolver : ITrieResolver<WsEndpointInfo>
    {
        public bool Succeeds { get; set; }
        public WsEndpointInfo? Value { get; set; }

        public bool TryResolve(ReadOnlyMemory<byte> sequence, out WsEndpointInfo? value)
        {
            value = Value;
            return Succeeds;
        }
    }

    private sealed class FakeAuthorizer : IEndpointAuthorizer
    {
        public Func<WsEndpointInfo, Task<Result<Success, Error<string>>>> Handler { get; set; }
            = _ => Task.FromResult(ResultOk());

        public Task<Result<Success, Error<string>>> AuthorizeAsync(
            IServiceProvider services, HttpContext context, WsEndpointInfo info)
            => Handler(info);
    }

    private static Result<Success, Error<string>> ResultOk() => new Success();
    private static Result<Success, Error<string>> ResultErr(string message) => new Error<string>(message);


    private sealed class Fixture : IDisposable
    {
        public FakeResolver Resolver { get; } = new();
        public FakeAuthorizer Authorizer { get; } = new();
        public IEndpointInvoker Invoker { get; } = Substitute.For<IEndpointInvoker>();
        public IWsConnectionManager Manager { get; } = Substitute.For<IWsConnectionManager>();
        public IContextFactory ContextFactory { get; } = Substitute.For<IContextFactory>();
        public IMessageSerializer Serializer { get; } = Substitute.For<IMessageSerializer>();
        public IMessageBuffer Buffer { get; } = Substitute.For<IMessageBuffer>();
        public IGlobalContext GlobalContext { get; } = Substitute.For<IGlobalContext>();

        public ServiceProvider Provider { get; }
        public WsEndpointInfo EndpointInfo { get; }
        public object EndpointInstance { get; } = new TestEndpoint();
        public DefaultMessageDispatcher Dispatcher { get; }

        public Fixture(bool withMetadata = false)
        {
            EndpointInfo = new WsEndpointInfo
            {
                Key = "k",
                Scope = ServiceLifetime.Scoped,
                Type = typeof(TestEndpoint),
                Metadata = withMetadata
                    ? new EndpointMetadataCollection(new object())
                    : EndpointMetadataCollection.Empty,
                Invoker = Invoker,
            };

            var services = new ServiceCollection();
            services.AddScoped(typeof(TestEndpoint), _ => EndpointInstance);
            services.AddSingleton<IEndpointAuthorizer>(Authorizer);
            Provider = services.BuildServiceProvider();

            var scopeFactory = Substitute.For<IServiceScopeFactory>();
            scopeFactory.CreateScope().Returns(_ => Provider.CreateScope());

            var conn = Substitute.For<IWsConnection>();
            conn.Id.Returns("conn-1");
            GlobalContext.Connection.Returns(conn);
            GlobalContext.Context.Returns(new DefaultHttpContext());

            Buffer.Memory.Returns(Memory<byte>.Empty);

            Manager
                .Send(Arg.Any<string>(), Arg.Any<WsMessage>(), Arg.Any<CancellationToken>())
                .Returns(ValueTask.CompletedTask);

            ContextFactory
                .Create(Arg.Any<IGlobalContext>(), Arg.Any<IServiceProvider>(),
                        Arg.Any<WsEndpointInfo>(), Arg.Any<MessageHeaders>(), Arg.Any<CancellationToken>())
                .Returns(Substitute.For<IEndpointContext>());

            Dispatcher = new DefaultMessageDispatcher(
                scopeFactory, ContextFactory, Resolver, Manager,
                NullLogger<DefaultMessageDispatcher>.Instance);
        }

        public Fixture WithHeaders(ReadOnlyMemory<byte> key, uint correlationId)
        {
            Serializer
                .ReadHeaders(Arg.Any<ReadOnlyMemory<byte>>())
                .Returns(new MessageHeaders(correlationId, key, default));
            return this;
        }

        public void Dispose() => Provider.Dispose();
    }

    private static bool IsErrorMessage(WsMessage m, WsErrorCode code) =>
        m is ErrorMessage e && e.Code == code;

    private static bool IsErrorMessage(WsMessage m, WsErrorCode code, uint correlationId) =>
        m is ErrorMessage e && e.Code == code && e.CorrelationId == correlationId;

    private static bool IsErrorMessage(WsMessage m, WsErrorCode code, string message) =>
        m is ErrorMessage e && e.Code == code && e.Message == message;

    [Fact]
    public async Task Empty_Key_Sends_UnknownRoute()
    {
        using var f = new Fixture();
        f.WithHeaders(ReadOnlyMemory<byte>.Empty, 5u);

        await f.Dispatcher.DispatchMessage(f.GlobalContext, f.Serializer, f.Buffer, CancellationToken.None);

        await f.Manager.Received(1).Send("conn-1",
            Arg.Is<WsMessage>(m => IsErrorMessage(m, WsErrorCode.UnknownRoute)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unresolvable_Key_Sends_UnknownRoute()
    {
        using var f = new Fixture();
        f.WithHeaders(new byte[] { 1 }, 5u);
        f.Resolver.Succeeds = false;

        await f.Dispatcher.DispatchMessage(f.GlobalContext, f.Serializer, f.Buffer, CancellationToken.None);

        await f.Manager.Received(1).Send("conn-1",
            Arg.Is<WsMessage>(m => IsErrorMessage(m, WsErrorCode.UnknownRoute)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Empty_Key_Propagates_Headers_CorrelationId()
    {
        using var f = new Fixture();
        f.WithHeaders(ReadOnlyMemory<byte>.Empty, 99u);

        await f.Dispatcher.DispatchMessage(f.GlobalContext, f.Serializer, f.Buffer, CancellationToken.None);

        await f.Manager.Received(1).Send("conn-1",
            Arg.Is<WsMessage>(m => IsErrorMessage(m, WsErrorCode.UnknownRoute, 99u)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Zero_CorrelationId_On_Error_Does_Not_Send()
    {
        using var f = new Fixture();
        f.WithHeaders(ReadOnlyMemory<byte>.Empty, 0u);

        await f.Dispatcher.DispatchMessage(f.GlobalContext, f.Serializer, f.Buffer, CancellationToken.None);

        await f.Manager.DidNotReceive()
            .Send(Arg.Any<string>(), Arg.Any<WsMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_Metadata_Skips_Authorizer_And_Invokes()
    {
        using var f = new Fixture(withMetadata: false);
        f.WithHeaders(new byte[] { 1 }, 5u);
        f.Resolver.Succeeds = true;
        f.Resolver.Value = f.EndpointInfo;

        var authorizerCalled = false;
        f.Authorizer.Handler = _ => { authorizerCalled = true; return Task.FromResult(ResultOk()); };

        await f.Dispatcher.DispatchMessage(f.GlobalContext, f.Serializer, f.Buffer, CancellationToken.None);

        Assert.False(authorizerCalled);
        await f.Invoker.Received(1).Invoke(Arg.Any<IEndpointContext>());
        await f.Manager.DidNotReceive()
            .Send(Arg.Any<string>(), Arg.Any<WsMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Metadata_Present_Authorizer_Success_Invokes_Endpoint()
    {
        using var f = new Fixture(withMetadata: true);
        f.WithHeaders(new byte[] { 1 }, 5u);
        f.Resolver.Succeeds = true;
        f.Resolver.Value = f.EndpointInfo;
        f.Authorizer.Handler = _ => Task.FromResult(ResultOk());

        await f.Dispatcher.DispatchMessage(f.GlobalContext, f.Serializer, f.Buffer, CancellationToken.None);

        await f.Invoker.Received(1).Invoke(Arg.Any<IEndpointContext>());
        await f.Manager.DidNotReceive()
            .Send(Arg.Any<string>(), Arg.Any<WsMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Metadata_Present_Authorizer_Failure_Sends_Unauthorized_And_Skips_Endpoint()
    {
        using var f = new Fixture(withMetadata: true);
        f.WithHeaders(new byte[] { 1 }, 5u);
        f.Resolver.Succeeds = true;
        f.Resolver.Value = f.EndpointInfo;
        f.Authorizer.Handler = _ => Task.FromResult(ResultErr("denied"));

        await f.Dispatcher.DispatchMessage(f.GlobalContext, f.Serializer, f.Buffer, CancellationToken.None);

        await f.Manager.Received(1).Send("conn-1",
            Arg.Is<WsMessage>(m => IsErrorMessage(m, WsErrorCode.Unauthorized, "denied")),
            Arg.Any<CancellationToken>());

        await f.Invoker.DidNotReceive().Invoke(Arg.Any<IEndpointContext>());
    }

    [Fact]
    public async Task Unhandled_Exception_Sends_Internal_Error()
    {
        using var f = new Fixture(withMetadata: false);
        f.WithHeaders(new byte[] { 1 }, 5u);
        f.Resolver.Succeeds = true;
        f.Resolver.Value = f.EndpointInfo;

        f.Invoker
            .When(x => x.Invoke(Arg.Any<IEndpointContext>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        await f.Dispatcher.DispatchMessage(f.GlobalContext, f.Serializer, f.Buffer, CancellationToken.None);

        await f.Manager.Received(1).Send("conn-1",
            Arg.Is<WsMessage>(m => IsErrorMessage(m, WsErrorCode.Internal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancellation_With_Cancelled_Token_Swallows_And_Does_Not_Send()
    {
        using var f = new Fixture(withMetadata: false);
        f.WithHeaders(new byte[] { 1 }, 5u);
        f.Resolver.Succeeds = true;
        f.Resolver.Value = f.EndpointInfo;

        f.Invoker
            .When(x => x.Invoke(Arg.Any<IEndpointContext>()))
            .Do(_ => throw new OperationCanceledException());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await f.Dispatcher.DispatchMessage(f.GlobalContext, f.Serializer, f.Buffer, cts.Token);

        await f.Manager.DidNotReceive()
            .Send(Arg.Any<string>(), Arg.Any<WsMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Exception_With_Cancelled_Token_Not_OCE_Still_Sends_Internal()
    {
        using var f = new Fixture(withMetadata: false);
        f.WithHeaders(new byte[] { 1 }, 5u);
        f.Resolver.Succeeds = true;
        f.Resolver.Value = f.EndpointInfo;

        f.Invoker
            .When(x => x.Invoke(Arg.Any<IEndpointContext>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await f.Dispatcher.DispatchMessage(f.GlobalContext, f.Serializer, f.Buffer, cts.Token);

        await f.Manager.Received(1).Send("conn-1",
            Arg.Is<WsMessage>(m => IsErrorMessage(m, WsErrorCode.Internal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Exception_With_Uncancelled_Token_Sends_Internal()
    {
        using var f = new Fixture(withMetadata: false);
        f.WithHeaders(new byte[] { 1 }, 5u);
        f.Resolver.Succeeds = true;
        f.Resolver.Value = f.EndpointInfo;

        f.Invoker
            .When(x => x.Invoke(Arg.Any<IEndpointContext>()))
            .Do(_ => throw new OperationCanceledException());

        await f.Dispatcher.DispatchMessage(f.GlobalContext, f.Serializer, f.Buffer, CancellationToken.None);

        await f.Manager.Received(1).Send("conn-1",
            Arg.Is<WsMessage>(m => IsErrorMessage(m, WsErrorCode.Internal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_Populates_All_Fields()
    {
        ReadOnlyMemory<byte> key = new byte[] { 1, 2, 3 };
        ReadOnlyMemory<byte> value = new byte[] { 4, 5, 6 };

        var headers = new MessageHeaders(42u, key, value);

        Assert.Equal(42u, headers.CorrelationId);
        Assert.Equal(key, headers.Key);
        Assert.Equal(value, headers.Value);
    }

    [Fact]
    public void Equality_Is_Structural()
    {
        ReadOnlyMemory<byte> key = new byte[] { 1 };
        ReadOnlyMemory<byte> value = new byte[] { 2 };

        var a = new MessageHeaders(1, key, value);
        var b = new MessageHeaders(1, key, value);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Key_And_Value_Can_Be_Empty()
    {
        var headers = new MessageHeaders(0, default, default);

        Assert.True(headers.Key.IsEmpty);
        Assert.True(headers.Value.IsEmpty);
    }

    [Fact]
    public async Task Zero_CorrelationId_Does_Not_Send()
    {
        var manager = Substitute.For<IWsConnectionManager>();

        await manager.TrySendErrorAsync("conn", 0u, WsErrorCode.Internal, "boom", CancellationToken.None);

        await manager.DidNotReceive()
            .Send(Arg.Any<string>(), Arg.Any<WsMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonZero_CorrelationId_Sends_ErrorMessage()
    {
        var manager = Substitute.For<IWsConnectionManager>();
        WsMessage? captured = null;

        manager
            .Send("conn", Arg.Do<WsMessage>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        await manager.TrySendErrorAsync("conn", 7u, WsErrorCode.Unauthorized, "denied", CancellationToken.None);

        var err = Assert.IsType<ErrorMessage>(captured);
        Assert.Equal(7u, err.CorrelationId);
        Assert.Equal(WsErrorCode.Unauthorized, err.Code);
        Assert.Equal("denied", err.Message);
    }

    [Fact]
    public async Task Forwards_ConnectionId_And_Token()
    {
        var manager = Substitute.For<IWsConnectionManager>();
        using var cts = new CancellationTokenSource();

        await manager.TrySendErrorAsync("conn-x", 1u, WsErrorCode.Internal, "x", cts.Token);

        await manager.Received(1).Send("conn-x", Arg.Any<WsMessage>(), cts.Token);
    }

    private static WsEndpointInfo NewInfo() => new()
    {
        Key = "key",
        Scope = ServiceLifetime.Singleton,
        Type = typeof(object),
        Metadata = EndpointMetadataCollection.Empty,
        Invoker = Substitute.For<IEndpointInvoker>(),
    };

    [Fact]
    public async Task Invokes_Factory_Once_And_Caches()
    {
        var info = NewInfo();
        var count = 0;

        Task<AuthorizationPolicy?> Factory(WsEndpointInfo _, IServiceProvider __)
        {
            Interlocked.Increment(ref count);
            return Task.FromResult<AuthorizationPolicy?>(null);
        }

        var provider = Substitute.For<IServiceProvider>();

        var first = await info.GetOrComputePolicy(Factory, provider);
        var second = await info.GetOrComputePolicy(Factory, provider);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Returns_Value_From_Factory_And_Caches()
    {
        var info = NewInfo();
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        var result = await info.GetOrComputePolicy(
            (_, _) => Task.FromResult<AuthorizationPolicy?>(policy),
            Substitute.For<IServiceProvider>());

        Assert.Same(policy, result);

        var result2 = await info.GetOrComputePolicy(
            (_, _) => Task.FromResult<AuthorizationPolicy?>(null),
            Substitute.For<IServiceProvider>());

        Assert.Same(policy, result2);
    }

    [Fact]
    public async Task Concurrent_Calls_Invoke_Factory_Once()
    {
        var info = NewInfo();
        var count = 0;
        var gate = new TaskCompletionSource<AuthorizationPolicy?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task<AuthorizationPolicy?> Factory(WsEndpointInfo _, IServiceProvider __)
        {
            Interlocked.Increment(ref count);
            return gate.Task;
        }

        var provider = Substitute.For<IServiceProvider>();
        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => info.GetOrComputePolicy(Factory, provider).AsTask()))
            .ToArray();

        await Task.Delay(50);
        gate.SetResult(null);
        await Task.WhenAll(tasks);

        Assert.Equal(1, count);
        Assert.All(tasks, t => Assert.Null(t.Result));
    }
}

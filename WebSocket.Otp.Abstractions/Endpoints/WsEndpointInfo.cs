using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace WebSockets.Otp.Abstractions.Endpoints;

public sealed class WsEndpointInfo
{
    public required string Key { get; init; }
    public required ServiceLifetime Scope { get; init; }
    public required Type Type { get; init; }
    public required EndpointMetadataCollection Metadata { get; init; }
    public required IEndpointInvoker Invoker { get; init; }

#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif

    private Task<AuthorizationPolicy?>? _policyTask;
    private AuthorizationPolicy? _policy;
    private volatile bool _policySet;

    public ValueTask<AuthorizationPolicy?> GetOrComputePolicy(
        Func<WsEndpointInfo, IServiceProvider, Task<AuthorizationPolicy?>> factory, IServiceProvider provider)
    {
        if (_policySet) return ValueTask.FromResult(_policy);

        lock (_gate)
        {
            _policyTask ??= RunAsync(factory, provider);

            return new ValueTask<AuthorizationPolicy?>(_policyTask);
        }
    }

    private async Task<AuthorizationPolicy?> RunAsync(
        Func<WsEndpointInfo, IServiceProvider, Task<AuthorizationPolicy?>> factory, IServiceProvider provider)
    {
        var policy = await factory(this, provider);
        _policy = policy;
        _policySet = true;
        return policy;
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Abstractions.Utils;

namespace WebSockets.Otp.Core.Services.Endpoints;

public sealed class EndpointAuthorizer : IEndpointAuthorizer
{
    public async Task<Result<Success, Error<string>>> AuthorizeAsync(
        IServiceProvider services, HttpContext context, WsEndpointInfo info)
    {
        var principal = context.User;
        if (principal is null)
            return new Error<string>("Authorization failed: user not authenticated.");

        var policy = await info.GetOrComputePolicy(BuildPolicy, services);
        if (policy is null)
            return new Success();

        var authzService = services.GetRequiredService<IAuthorizationService>();
        var authzResult = await authzService.AuthorizeAsync(principal, resource: null, policy);

        if (authzResult.Succeeded)
            return new Success();

        var failed = authzResult.Failure?.FailedRequirements
            .Select(r => r.ToString() ?? r.GetType().Name)
            .ToArray() ?? [];

        return new Error<string>($"Authorization failed. Reason={string.Join(", ", failed)}");
    }

    private static Task<AuthorizationPolicy?> BuildPolicy(WsEndpointInfo endpointInfo, IServiceProvider sp)
    {
        var endpoint = endpointInfo.Metadata;
        if (endpoint is { Count: 0 })
            return Task.FromResult<AuthorizationPolicy?>(null);

        var authorizeData = endpoint.GetOrderedMetadata<IAuthorizeData>();
        if (authorizeData is { Count: 0 })
            return Task.FromResult<AuthorizationPolicy?>(null);

        var policyProvider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
        return AuthorizationPolicy.CombineAsync(policyProvider, authorizeData, []);
    }
}
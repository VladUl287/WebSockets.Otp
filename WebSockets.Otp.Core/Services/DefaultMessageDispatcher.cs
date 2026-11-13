using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using WebSockets.Otp.Abstractions.Connections;
using WebSockets.Otp.Abstractions.Contracts;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Abstractions.Options;
using WebSockets.Otp.Abstractions.Serializers;
using WebSockets.Otp.Abstractions.Transport;
using WebSockets.Otp.Abstractions.Utils;
using WebSockets.Otp.Core.Logging;

namespace WebSockets.Otp.Core.Services;

public class DefaultMessageDispatcher(
    IServiceScopeFactory scopeFactory, IContextFactory contextFactory, ITrieResolver<WsEndpointInfo> endpointResolver,
    IWsConnectionManager manager, ILogger<DefaultMessageDispatcher> logger) : IMessageDispatcher
{
    public async Task DispatchMessage(IGlobalContext context, IMessageSerializer serializer, IMessageBuffer payload, CancellationToken token)
    {
        var messageHeader = serializer.ReadHeaders(payload.Memory);

        if (!TryResolveEndpoint(messageHeader, out var endpointInfo))
            return;

        await using var scope = scopeFactory.CreateAsyncScope();

        if (!await TryAuthorizeAsync(scope.ServiceProvider, context, messageHeader, endpointInfo, token))
            return;

        await InvokeEndpointAsync(scope.ServiceProvider, context, messageHeader, endpointInfo, token);
    }

    private bool TryResolveEndpoint(MessageHeaders messageHeader, [NotNullWhen(true)] out WsEndpointInfo? endpointInfo)
    {
        endpointInfo = null;

        var keySlice = messageHeader.Key;

        if (keySlice.IsEmpty || !endpointResolver.TryResolve(keySlice, out endpointInfo))
        {
            logger.KeyFieldMissing();
            return false;
        }

        return true;
    }

    private async Task<bool> TryAuthorizeAsync(
        IServiceProvider services, IGlobalContext context, MessageHeaders messageHeader,
        WsEndpointInfo endpointInfo, CancellationToken token)
    {
        if (endpointInfo.Endpoint is null)
            return true;

        var authorizer = services.GetRequiredService<IEndpointAuthorizer>();
        var result = await authorizer.AuthorizeAsync(context.Context, endpointInfo);

        if (!result.IsError)
        {
            return true;
        }

        logger.AuthorizationFailed(result.Error);

        if (messageHeader.Id != default)
        {
            var envelope = new ErrorMessage(messageHeader.Id, result.Error);
            await manager.Send(context.Connection.Id, envelope, token);
        }

        return false;
    }

    private async Task InvokeEndpointAsync(
        IServiceProvider services, IGlobalContext context, MessageHeaders messageHeader,
        WsEndpointInfo endpointInfo, CancellationToken token)
    {
        var endpoint = services.GetRequiredService(endpointInfo.EndpointType);
        var endpointContext = contextFactory.Create(messageHeader.Id, context, messageHeader.Value, token);
        await endpointInfo.Invoker.Invoke(endpoint, endpointContext);
    }
}

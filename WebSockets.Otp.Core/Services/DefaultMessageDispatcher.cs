using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Contracts;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Abstractions.Serializers;
using WebSockets.Otp.Abstractions.Transport;
using WebSockets.Otp.Abstractions.Utils;
using WebSockets.Otp.Core.Extensions;
using WebSockets.Otp.Core.Logging;

namespace WebSockets.Otp.Core.Services;

public class DefaultMessageDispatcher(
    IServiceScopeFactory scopeFactory, IContextFactory contextFactory, ITrieResolver<WsEndpointInfo> endpointResolver,
    IWsConnectionManager manager, ILogger<DefaultMessageDispatcher> logger) : IMessageDispatcher
{
    public async Task DispatchMessage(
        IGlobalContext context, IMessageSerializer serializer, IMessageBuffer payload, CancellationToken token)
    {
        var headers = serializer.ReadHeaders(payload.Memory);

        try
        {
            if (headers.Key.IsEmpty || !endpointResolver.TryResolve(headers.Key, out var endpointInfo))
            {
                logger.KeyFieldMissing();

                await manager.TrySendErrorAsync(
                    context.Connection.Id, headers.CorrelationId, WsErrorCode.UnknownRoute, "Endpoint not found", token);
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;

            if (endpointInfo is { Metadata.Count: > 0 })
            {
                var result = await services
                    .GetRequiredService<IEndpointAuthorizer>()
                    .AuthorizeAsync(context.Context, endpointInfo);

                if (result.IsError)
                {
                    logger.AuthorizationFailed(result.Error);

                    await manager.TrySendErrorAsync(
                        context.Connection.Id, headers.CorrelationId, WsErrorCode.Unauthorized, result.Error, token);
                }
            }

            var endpoint = services.GetRequiredService(endpointInfo.Type);
            var endpointContext = contextFactory.Create(context, services, endpointInfo, headers, token);
            await endpointInfo.Invoker.Invoke(endpoint, endpointContext);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.InternalError(context.Context, ex);

            await manager.TrySendErrorAsync(
                context.Connection.Id, headers.CorrelationId, WsErrorCode.Internal, "An error occurred while processing the message.", token);
        }
    }
}

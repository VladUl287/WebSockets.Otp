using Microsoft.Extensions.DependencyInjection;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Abstractions.Utils;
using WebSockets.Otp.Core.Extensions;

namespace WebSockets.Otp.Core.Services.Endpoints;

public sealed class RequestTypedResponseEndpointInvoker<TWsEndpoint, TRequest, TResponse1, TResponse2> : IEndpointInvoker
    where TWsEndpoint : WsEndpoint<TRequest, Result<TResponse1, TResponse2>>
    where TRequest : notnull
    where TResponse1 : notnull
    where TResponse2 : notnull
{
    public async Task Invoke(IEndpointContext context)
    {
        var connection = context.Connection;
        var token = context.Token;
        var correlationId = context.MessageHeaders.CorrelationId;

        var request = connection.Serializer.Deserialize<TRequest>(context.Message);
        if (request is null)
        {
            await context.Manager.TrySendErrorAsync(
                connection.Id, correlationId, WsErrorCode.InvalidPayload, $"Deserialization returned null for {typeof(TRequest).Name} at endpoint {typeof(TWsEndpoint).Name}.", token);
            return;
        }

        var response = await context.Services
            .GetRequiredService<TWsEndpoint>()
            .HandleAsync(request, context);

        if (response.IsError)
        {
            var errorResponse = new ResponseMessage(correlationId, response.Error);
            await context.Manager.Send(connection.Id, errorResponse, token);
            return;
        }

        var successResponse = new ResponseMessage(correlationId, response.Value);
        await context.Manager.Send(connection.Id, successResponse, token);
    }
}

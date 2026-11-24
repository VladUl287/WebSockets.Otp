using Microsoft.Extensions.DependencyInjection;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Core.Extensions;

namespace WebSockets.Otp.Core.Services.Endpoints;

public sealed class RequestResponseEndpointInvoker<TWsEndpoint, TRequest, TResponse> : IEndpointInvoker
    where TWsEndpoint : WsEndpoint<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
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

        var envelope = new ResponseMessage(correlationId, response);
        await context.Manager.Send(connection.Id, envelope, token);
    }
}

using System.Runtime.CompilerServices;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Abstractions.Utils;
using WebSockets.Otp.Core.Extensions;

namespace WebSockets.Otp.Core.Services.Endpoints;

public sealed class RequestTypedResponseEndpointInvoker<TRequest, TResponse1, TResponse2> : IEndpointInvoker
    where TRequest : notnull
    where TResponse1 : notnull
    where TResponse2 : notnull
{
    public async Task Invoke(object endpoint, IEndpointContext context)
    {
        var typedEndpoint = Unsafe.As<WsEndpoint<TRequest, Result<TResponse1, TResponse2>>>(endpoint);

        var connection = context.Connection;
        var serializer = connection.Serializer;
        var payload = context.Message;
        var token = context.Token;

        var correlationId = context.MessageHeaders.CorrelationId;
        var request = serializer.Deserialize<TRequest>(payload);
        if (request is null)
        {
            await context.Manager.TrySendErrorAsync(
                connection.Id, correlationId, WsErrorCode.InvalidPayload, $"Deserialization returned null for {typeof(TRequest).Name} at endpoint {endpoint.GetType().Name}.", token);
            return;
        }

        var response = await typedEndpoint.HandleAsync(request, context);
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

using System.Runtime.CompilerServices;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Core.Extensions;

namespace WebSockets.Otp.Core.Services.Endpoints;

public sealed class RequestResponseEndpointInvoker<TRequest, TResponse> : IEndpointInvoker
    where TRequest : notnull
    where TResponse : notnull
{
    public async Task Invoke(object endpoint, IEndpointContext context)
    {
        var typedEndpoint = Unsafe.As<WsEndpoint<TRequest, TResponse>>(endpoint);

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

        if (response is EndpointResult<TResponse> result)
        {
            if (result.IsError)
            {
                await context.Manager.TrySendErrorAsync(
                    connection.Id, correlationId, WsErrorCode.HandlerFailed, result.Error, token);
            }
            if (result.IsSuccess)
            {
                var value = new ResponseMessage(correlationId, result.Value);
                await context.Manager.Send(connection.Id, value, context.Token);
            }
        }

        var envelope = new ResponseMessage(correlationId, response);
        await context.Manager.Send(connection.Id, envelope, context.Token);
    }
}

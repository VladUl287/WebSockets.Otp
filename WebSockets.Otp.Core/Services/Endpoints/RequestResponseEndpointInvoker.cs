using System.Runtime.CompilerServices;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Endpoints;

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

        var correlationId = context.MessageHeaders.CorrelationId;
        var request = serializer.Deserialize<TRequest>(payload);
        if (request is null)
        {
            var envelope = new ErrorMessage(
                correlationId, WsErrorCode.InvalidPayload, $"Deserialization returned null for {typeof(TRequest).Name} at endpoint {endpoint.GetType().Name}.");
            await context.Manager.Send(connection.Id, envelope, context.Token);
            return;
        }

        try
        {
            var response = await typedEndpoint.HandleAsync(request, context);

            if (response is EndpointResult<TResponse> result)
            {
                if (result.IsError)
                {
                    var error = new ErrorMessage(correlationId, WsErrorCode.HandlerFailed, result.Error);
                    await context.Manager.Send(connection.Id, error, context.Token);
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
        catch (Exception ex)
        {
            var envelope = new ErrorMessage(correlationId, WsErrorCode.HandlerFailed, ex.Message);
            await context.Manager.Send(connection.Id, envelope, context.Token);
        }
    }
}

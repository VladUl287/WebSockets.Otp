using System.Runtime.CompilerServices;
using WebSockets.Otp.Abstractions;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Abstractions.Options;
using WebSockets.Otp.Abstractions.Utils;

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
        var payload = context.Payload;

        var request = serializer.Deserialize<TRequest>(payload);
        if (request is null)
        {
            var envelope = new ErrorMessage(
                context.CorrelationId, WsErrorCode.InvalidPayload, $"Deserialization returned null for {typeof(TRequest).Name} at endpoint {endpoint.GetType().Name}.");
            await context.Manager.Send(connection.Id, envelope, context.Cancellation);
            return;
        }

        try
        {
            var response = await typedEndpoint.HandleAsync(request, context);

            if (response is EndpointResult<TResponse> result)
            {
                if (result.IsError)
                {
                    var error = new ErrorMessage(context.CorrelationId, WsErrorCode.HandlerFailed, result.Error);
                    await context.Manager.Send(connection.Id, error, context.Cancellation);
                }
                if (result.IsSuccess)
                {
                    var value = new ResponseMessage(context.CorrelationId, result.Value);
                    await context.Manager.Send(connection.Id, value, context.Cancellation);
                }
            }

            var envelope = new ResponseMessage(context.CorrelationId, response);
            await context.Manager.Send(connection.Id, envelope, context.Cancellation);
        }
        catch (Exception ex)
        {
            var envelope = new ErrorMessage(context.CorrelationId, WsErrorCode.HandlerFailed, ex.Message);
            await context.Manager.Send(connection.Id, envelope, context.Cancellation);
        }
    }
}

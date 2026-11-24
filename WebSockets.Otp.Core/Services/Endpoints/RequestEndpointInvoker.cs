using Microsoft.Extensions.DependencyInjection;
using WebSockets.Otp.Abstractions.Endpoints;

namespace WebSockets.Otp.Core.Services.Endpoints;

public sealed class RequestEndpointInvoker<TWsEndpoint, TRequest> : IEndpointInvoker
    where TWsEndpoint : WsEndpoint<TRequest>
    where TRequest : notnull
{
    public Task Invoke(IEndpointContext context)
    {
        var request = context.Connection.Serializer.Deserialize<TRequest>(context.Message) ??
             throw new NullReferenceException(
                $"Deserialization returned null for {typeof(TRequest).Name} at endpoint {typeof(TWsEndpoint).Name}.");

        return context.Services
            .GetRequiredService<TWsEndpoint>()
            .HandleAsync(request, context);
    }
}

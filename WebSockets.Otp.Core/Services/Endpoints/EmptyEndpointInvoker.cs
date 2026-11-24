using Microsoft.Extensions.DependencyInjection;
using WebSockets.Otp.Abstractions.Endpoints;

namespace WebSockets.Otp.Core.Services.Endpoints;

public sealed class EmptyEndpointInvoker<TWsEndpoint> : IEndpointInvoker
    where TWsEndpoint : WsEndpoint
{
    public Task Invoke(IEndpointContext context)
    {
        return context.Services
            .GetRequiredService<TWsEndpoint>()
            .HandleAsync(context);
    }
}

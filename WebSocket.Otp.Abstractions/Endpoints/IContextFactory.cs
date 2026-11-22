using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using WebSockets.Otp.Abstractions.Connection;

namespace WebSockets.Otp.Abstractions.Endpoints;

public interface IContextFactory
{
    IGlobalContext CreateGlobal(IWsConnection connection, HttpContext context, WebSocket socket);
    IEndpointContext Create(
        IGlobalContext global, IServiceProvider services, WsEndpointInfo endpoint, MessageHeaders headers, CancellationToken token);
}

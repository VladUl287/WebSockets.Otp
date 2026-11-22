using Microsoft.AspNetCore.Http;
using System.Net.WebSockets;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Core.Models;

namespace WebSockets.Otp.Core.Services;

public sealed class DefaultContextFactory(IWsConnectionManager manager) : IContextFactory
{
    public IGlobalContext CreateGlobal(IWsConnection connection, HttpContext context, WebSocket socket) => 
        new GlobalContext(connection, manager, context, socket);

    public IEndpointContext Create(
        IGlobalContext global, IServiceProvider services, WsEndpointInfo endpoint, MessageHeaders headers, CancellationToken token) => 
        new EndpointContext(global, services, endpoint, headers, token);
}

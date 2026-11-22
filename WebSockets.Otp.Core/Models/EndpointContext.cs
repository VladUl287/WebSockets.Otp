using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Abstractions.Utils.Managers;

namespace WebSockets.Otp.Core.Models;

public sealed class EndpointContext(
    IGlobalContext context, IServiceProvider services, WsEndpointInfo endpoint, 
    MessageHeaders headers, CancellationToken token) : IEndpointContext
{
    public IWsConnection Connection => context.Connection;
    public IWsConnectionManager Manager => context.Manager;

    public HttpContext Context => context.Context;
    public WebSocket Socket => context.Socket;

    public IServiceProvider Services => services;
    public WsEndpointInfo Endpoint => endpoint;
    public CancellationToken Token => token;

    public MessageHeaders MessageHeaders => headers;
    public ReadOnlyMemory<byte> Message => headers.Value;

    public SendManager Send => new(Manager);
    public GroupManager Group => new(Manager);
}

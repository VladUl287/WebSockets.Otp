using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Endpoints;

namespace WebSockets.Otp.Core.Models;

public sealed class GlobalContext(
    IWsConnection connection,
    IWsConnectionManager manager,
    HttpContext context,
    WebSocket socket) : IGlobalContext
{
    public IWsConnection Connection => connection;

    public IWsConnectionManager Manager => manager;

    public HttpContext Context => context;

    public WebSocket Socket => socket;
}

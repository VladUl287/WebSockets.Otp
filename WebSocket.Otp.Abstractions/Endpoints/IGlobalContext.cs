using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using WebSockets.Otp.Abstractions.Connection;

namespace WebSockets.Otp.Abstractions.Endpoints;

public interface IGlobalContext
{
    IWsConnection Connection { get; }
    IWsConnectionManager Manager { get; }

    HttpContext Context { get; }
    WebSocket Socket { get; }
}

using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using WebSockets.Otp.Abstractions.Utils;
using WebSockets.Otp.Abstractions.Options;

namespace WebSockets.Otp.Abstractions.Contracts;

public interface IHandshakeHandler
{
    ValueTask<Result<HandshakeOptions, Error>> HandleAsync(
        HttpContext context, WebSocket socket, WsOptionsSnapshot config, CancellationToken token);
}

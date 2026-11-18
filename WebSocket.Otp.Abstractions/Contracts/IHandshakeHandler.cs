using Microsoft.AspNetCore.Http;
using System.Net.WebSockets;
using WebSockets.Otp.Abstractions.Options;
using WebSockets.Otp.Abstractions.Utils;

namespace WebSockets.Otp.Abstractions.Contracts;

public interface IHandshakeHandler
{
    ValueTask<Result<HandshakeOptions, HandshakeError>> HandleAsync(
        HttpContext context, WebSocket socket, WsOptionsSnapshot config, CancellationToken token);
}

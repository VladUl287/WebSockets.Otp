using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using WebSockets.Otp.Abstractions.Utils;
using WebSockets.Otp.Abstractions.Options;

namespace WebSockets.Otp.Abstractions.Contracts;

public interface IHandshakeHandler
{
    ValueTask<Result<WsHandshakeOptions, Error>> HandleAsync(
        HttpContext context, WebSocket socket, WsOptions config, CancellationToken token);
}

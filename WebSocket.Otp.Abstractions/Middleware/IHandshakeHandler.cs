using System.Net.WebSockets;
using WebSockets.Otp.Abstractions.Utils;
using WebSockets.Otp.Abstractions.Options;

namespace WebSockets.Otp.Abstractions.Middleware;

public interface IHandshakeHandler
{
    ValueTask<Result<WsHandshakeOptions, Error<string>>> HandleAsync(WebSocket socket, WsOptions config, CancellationToken token);
}

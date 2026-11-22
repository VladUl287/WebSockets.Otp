using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using WebSockets.Otp.Abstractions.Contracts;
using WebSockets.Otp.Abstractions.Options;
using WebSockets.Otp.Abstractions.Serializers;
using WebSockets.Otp.Abstractions.Transport;
using WebSockets.Otp.Abstractions.Utils;
using WebSockets.Otp.Core.Extensions;
using WebSockets.Otp.Core.Logging;
using WebSockets.Otp.Core.Utils;

namespace WebSockets.Otp.Core.Services;

public sealed class DefaultHandshakeHandler(
    ISerializerStore store, IMessageEnumerator enumerator, ILogger<DefaultHandshakeHandler> logger) : IHandshakeHandler
{
    private const string Protocol = "json";
    private static readonly ReadOnlyMemory<byte> ResponseBytes = "{}"u8.ToArray();

    public async ValueTask<Result<WsHandshakeOptions, Error>> HandleAsync(
        HttpContext context, WebSocket socket, WsOptions options, CancellationToken token)
    {
        var requestId = new RequestId(context);

        var messages = enumerator.EnumerateAsync(socket, options, token);

        var handshakeBuffer = await AsyncEnumerableExtensions.FirstOrDefaultAsync(messages, token);
        if (handshakeBuffer is null)
        {
            logger.HandshakeNoMessage(requestId);
            return new Error();
        }

        try
        {
            if (!store.TryGet(Protocol, out var serializer))
            {
                logger.HandshakeUnsupportedProtocol(Protocol, requestId);
                return new Error();
            }

            var handshakeOptions = serializer.Deserialize<WsHandshakeOptions>(handshakeBuffer.Memory);
            if (handshakeOptions is null)
            {
                logger.HandshakeDeserializeFailed(requestId);
                return new Error();
            }

            await socket.SendAsync(ResponseBytes, WebSocketMessageType.Text, true, token);
            return handshakeOptions;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.HandshakeSendFailed(ex, requestId);
            return new Error();
        }
        finally
        {
            enumerator.ReturnBuffer(handshakeBuffer);
        }
    }
}

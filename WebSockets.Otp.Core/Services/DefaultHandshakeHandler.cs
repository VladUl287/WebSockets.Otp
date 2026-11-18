using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using WebSockets.Otp.Abstractions.Contracts;
using WebSockets.Otp.Abstractions.Options;
using WebSockets.Otp.Abstractions.Serializers;
using WebSockets.Otp.Abstractions.Transport;
using WebSockets.Otp.Abstractions.Utils;
using WebSockets.Otp.Core.Extensions;

namespace WebSockets.Otp.Core.Services;

public sealed class DefaultHandshakeHandler(ISerializerStore store, IMessageEnumerator enumerator) : IHandshakeHandler
{
    private const string Protocol = "json";
    private static readonly ReadOnlyMemory<byte> ResponseBytes = "{}"u8.ToArray();

    public async ValueTask<Result<HandshakeOptions, HandshakeError>> HandleAsync(
        HttpContext context, WebSocket socket, WsOptionsSnapshot options, CancellationToken token)
    {
        var messages = enumerator.EnumerateAsync(socket, options, token);

        var handshakeBuffer = await AsyncEnumerableExtensions.FirstOrDefaultAsync(messages, token);
        if (handshakeBuffer is null)
            return new HandshakeError.NoMessage();

        try
        {
            if (!store.TryGet(Protocol, out var serializer))
                return new HandshakeError.UnsupportedProtocol(Protocol);

            var handshakeOptions = serializer.Deserialize<HandshakeOptions>(handshakeBuffer.Memory);
            if (handshakeOptions is null)
                return new HandshakeError.DeserializeFailed();

            await socket.SendAsync(ResponseBytes, WebSocketMessageType.Text, true, token);
            return handshakeOptions;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HandshakeError.SendFailed(ex);
        }
        finally
        {
            enumerator.ReturnBuffer(handshakeBuffer);
        }
    }
}

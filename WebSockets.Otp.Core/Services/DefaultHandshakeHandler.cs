using System.Net.WebSockets;
using System.Text.Json;
using WebSockets.Otp.Abstractions.Middleware;
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
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async ValueTask<Result<WsHandshakeOptions, Error<string>>> HandleAsync(
        WebSocket socket, WsOptions options, CancellationToken token)
    {
        var messages = enumerator.EnumerateAsync(socket, options, token);
        var handshakeBuffer = await AsyncEnumerableExtensions.FirstOrDefaultAsync(messages, token);

        if (handshakeBuffer is null)
            return new Error<string>("Handshake failed: no initial message received.");

        try
        {
            if (!store.TryGet(Protocol, out var serializer))
                return new Error<string>($"Handshake failed: unsupported protocol '{Protocol}'.");

            var handshakeOptions = serializer.Deserialize<WsHandshakeOptions>(handshakeBuffer.Memory, DefaultOptions);

            if (handshakeOptions is null)
                return new Error<string>("Handshake failed: could not deserialize handshake options.");

            if (!store.TryGet(handshakeOptions.Protocol, out _))
                return new Error<string>($"Handshake failed: unsupported transfer protocol '{handshakeOptions.Protocol}'.");

            await socket.SendAsync(ResponseBytes, WebSocketMessageType.Text, true, token);
            return handshakeOptions;
        }
        finally
        {
            enumerator.ReturnBuffer(handshakeBuffer);
        }
    }
}

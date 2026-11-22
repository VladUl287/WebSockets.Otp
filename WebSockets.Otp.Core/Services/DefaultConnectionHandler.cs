using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Contracts;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Abstractions.Options;
using WebSockets.Otp.Abstractions.Serializers;
using WebSockets.Otp.Abstractions.Transport;
using WebSockets.Otp.Core.Logging;

namespace WebSockets.Otp.Core.Services;

public sealed class DefaultConnectionHandler(
    IWsConnectionManager connectionManager, IWsConnectionFactory connectionFactory, IHandshakeHandler handshakeService,
    IContextFactory contextFactory, IMessageProcessor messageProcessor, ISerializerStore serializerStore,
    ILogger<DefaultConnectionHandler> logger) : IConnectionHandler
{
    public async Task HandleAsync(HttpContext context, WsOptions options)
    {
        var token = context.RequestAborted;
        if (token.IsCancellationRequested)
            return;

        using var socket = await context.WebSockets.AcceptWebSocketAsync();

        var serializer = await NegotiateAsync(context, socket, options, token);
        if (serializer is null)
            return;

        var connection = connectionFactory.Create(socket, serializer);
        if (!await connectionManager.TryAdd(connection, token))
        {
            logger.ConnectionAddFailed(connection.Id, context);
            return;
        }

        logger.ConnectionEstablished(connection.Id, context);

        await RunSessionAsync(connection, serializer, context, socket, options, token);
    }

    private async Task<IMessageSerializer?> NegotiateAsync(
       HttpContext context, WebSocket socket, WsOptions options, CancellationToken token)
    {
        var handshakeResult = await handshakeService.HandleAsync(context, socket, options, token);
        if (handshakeResult.IsError)
            return null;

        var protocol = handshakeResult.Value.Protocol;
        logger.HandshakeCompleted(protocol, context);

        if (!serializerStore.TryGet(protocol, out var serializer))
        {
            logger.SerializerNotFound(protocol, context);
            return null;
        }

        return serializer;
    }

    private async Task RunSessionAsync(
        IWsConnection connection, IMessageSerializer serializer, HttpContext context,
        WebSocket socket, WsOptions options, CancellationToken token)
    {
        var globalContext = contextFactory.CreateGlobal(connection, context, socket);
        try
        {
            options.OnConnected?.Invoke(globalContext);
            await messageProcessor.Process(globalContext, serializer, options, token);
        }
        finally
        {
            await connectionManager.TryRemove(connection.Id, token);
            options.OnDisconnected?.Invoke(globalContext);
        }
    }
}

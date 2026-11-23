using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Contracts;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Abstractions.Middleware;
using WebSockets.Otp.Abstractions.Options;
using WebSockets.Otp.Abstractions.Serializers;
using WebSockets.Otp.Abstractions.Transport;
using WebSockets.Otp.Abstractions.Utils;
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

        var negotiateResult = await NegotiateAsync(context, socket, options, token);
        if (negotiateResult.IsError)
            return;

        var serializer = negotiateResult.Value;
        var connection = connectionFactory.Create(socket, serializer);
        if (!await connectionManager.TryAdd(connection, token))
        {
            logger.ConnectionAddFailed(connection.Id, context);
            return;
        }

        logger.ConnectionEstablished(connection.Id, context);

        await RunSessionAsync(connection, serializer, context, socket, options, token);
    }

    private async ValueTask<Result<IMessageSerializer, Error>> NegotiateAsync(
       HttpContext context, WebSocket socket, WsOptions options, CancellationToken token)
    {
        try
        {
            var handshake = await handshakeService.HandleAsync(socket, options, token);
            if (handshake.IsError)
            {
                logger.HandshakeFailed(handshake.Error.Value, context);
                return new Error();
            }

            var protocol = handshake.Value.Protocol;
            if (!serializerStore.TryGet(protocol, out var serializer))
            {
                logger.SerializerNotFound(protocol, context);
                return new Error();
            }

            logger.HandshakeCompleted(protocol, context);
            return Result<IMessageSerializer, Error>.Ok(serializer);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            logger.HandshakeCancelled(context);
            return new Error();
        }
        catch (Exception ex)
        {
            logger.HandshakeThrew(ex, context);
            return new Error();
        }
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

using Microsoft.Extensions.Logging;
using WebSockets.Otp.Core.Utils;

namespace WebSockets.Otp.Core.Logging;

internal static partial class DefaultConnectionHandlerLogging
{
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Handshake completed. Protocol='{Protocol}'. RequestId='{RequestId}'")]
    public static partial void HandshakeCompleted(
        this ILogger logger, string protocol, RequestId requestId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Handshake failed. Reason='{Reason}'. RequestId='{RequestId}'")]
    public static partial void HandshakeFailed(
        this ILogger logger, string reason, RequestId requestId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Handshake threw an unexpected exception. RequestId='{RequestId}'")]
    public static partial void HandshakeThrew(
        this ILogger logger, Exception cause, RequestId requestId);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Handshake cancelled: client disconnected. RequestId='{RequestId}'")]
    public static partial void HandshakeCancelled(
        this ILogger logger, RequestId requestId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Serializer not found for protocol='{Protocol}'. RequestId='{RequestId}'")]
    public static partial void SerializerNotFound(
        this ILogger logger, string protocol, RequestId requestId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Connection established. ConnectionId='{ConnectionId}'. RequestId='{RequestId}'")]
    public static partial void ConnectionEstablished(
        this ILogger logger, string connectionId, RequestId requestId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to add connection. ConnectionId='{ConnectionId}'. RequestId='{RequestId}'")]
    public static partial void ConnectionAddFailed(
        this ILogger logger, string connectionId, RequestId requestId);
}

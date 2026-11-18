using Microsoft.Extensions.Logging;
using WebSockets.Otp.Core.Utils;

namespace WebSockets.Otp.Core.Logging;

internal static partial class DefaultConnectionHandlerLogging
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "WebSocket request processing started. TraceId - '{traceId}'")]
    public static partial void RequestProcessingStarted(this ILogger logger, RequestId traceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handshake options not found. TraceId - '{traceId}'")]
    public static partial void HandshakeOptionsNotFound(this ILogger logger, RequestId traceId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handshake completed. Protocol - '{protocol}'. TraceId - '{traceId}'")]
    public static partial void HandshakeCompleted(this ILogger logger, string protocol, RequestId traceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Serializer not found for protocol. Protocol - '{protocol}'. TraceId - '{traceId}'")]
    public static partial void SerializerNotFound(this ILogger logger, string protocol, RequestId traceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to add connection. ConnectionId - '{ConnectionId}'. TraceId - '{traceId}'")]
    public static partial void ConnectionAddFailed(this ILogger logger, string connectionId, RequestId traceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection established. ConnectionId - '{ConnectionId}'. TraceId - '{traceId}'")]
    public static partial void ConnectionEstablished(this ILogger logger, string connectionId, RequestId traceId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invoking OnConnected callback. ConnectionId - '{ConnectionId}'. TraceId - '{traceId}'")]
    public static partial void InvokingOnConnectedCallback(this ILogger logger, string connectionId, RequestId traceId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting message processing. TraceId - '{traceId}'")]
    public static partial void MessageProcessingStarted(this ILogger logger, string connectionId, RequestId traceId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Message processing completed. ConnectionId - '{ConnectionId}'. TraceId - '{traceId}'")]
    public static partial void MessageProcessingCompleted(this ILogger logger, string connectionId, RequestId traceId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removing connection. ConnectionId - '{ConnectionId}'. TraceId - '{traceId}'")]
    public static partial void RemovingConnection(this ILogger logger, string connectionId, RequestId traceId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invoking OnDisconnected callback. ConnectionId - '{ConnectionId}'. TraceId - '{traceId}'")]
    public static partial void InvokingOnDisconnectedCallback(this ILogger logger, string connectionId, RequestId traceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection closed. ConnectionId - '{ConnectionId}'. TraceId - '{traceId}'")]
    public static partial void ConnectionClosed(this ILogger logger, string connectionId, RequestId traceId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handshake aborted: no message received. RequestId={RequestId}")]
    public static partial void HandshakeNoMessage(this ILogger logger, RequestId requestId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handshake rejected: unsupported protocol '{Protocol}'. RequestId={RequestId}")]
    public static partial void HandshakeUnsupportedProtocol(this ILogger logger, string protocol, RequestId requestId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handshake rejected: payload could not be deserialized. RequestId={RequestId}")]
    public static partial void HandshakeDeserializeFailed(this ILogger logger, RequestId requestId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handshake rejected: failed to send acknowledgement. RequestId={RequestId}")]
    public static partial void HandshakeSendFailed(this ILogger logger, Exception cause, RequestId requestId);
}

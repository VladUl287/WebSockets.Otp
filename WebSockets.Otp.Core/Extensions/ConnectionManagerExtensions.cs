using WebSockets.Otp.Abstractions.Connection;

namespace WebSockets.Otp.Core.Extensions;

public static class ConnectionManagerExtensions
{
    public static ValueTask TrySendErrorAsync(
       this IWsConnectionManager manager, string connectionId, CorrelationId correlationId,
       WsErrorCode code, string message, CancellationToken token)
    {
        if (correlationId.IsEmpty)
            return ValueTask.CompletedTask;

        return manager.Send(connectionId, new ErrorMessage(correlationId, code, message), token);
    }
}

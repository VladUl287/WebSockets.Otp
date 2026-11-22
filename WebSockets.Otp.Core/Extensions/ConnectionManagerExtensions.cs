using WebSockets.Otp.Abstractions.Connection;

namespace WebSockets.Otp.Core.Extensions;

internal static class ConnectionManagerExtensions
{
    internal static ValueTask TrySendErrorAsync(
       this IWsConnectionManager manager, string connectionId, uint correlationId,
       WsErrorCode code, string message, CancellationToken token)
    {
        if (correlationId == 0u)
            return ValueTask.CompletedTask;

        return manager.Send(connectionId, new ErrorMessage(correlationId, code, message), token);
    }
}

using Microsoft.Extensions.Logging;
using WebSockets.Otp.Core.Utils;

namespace WebSockets.Otp.Core.Logging;

internal static partial class DefaultMessageDispatcherLogging
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Authorization failed. Reason: '{Reason}'")]
    internal static partial void AuthorizationFailed(this ILogger logger, string Reason);
    
    [LoggerMessage(Level = LogLevel.Error, Message = "Message key field is required")]
    internal static partial void KeyFieldMissing(this ILogger logger);
    
    [LoggerMessage(Level = LogLevel.Error, Message = "Handler failed for {RequestId} with {Exception}")]
    internal static partial void InternalError(this ILogger logger, RequestId RequestId, Exception Exception);
}

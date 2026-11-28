using WebSockets.Otp.Abstractions.Utils;

namespace WebSockets.Otp.Abstractions.Connection;

public interface IWsConnectionManager
{
    ValueTask<bool> TryAdd(IWsConnection connection, CancellationToken token);
    ValueTask<bool> TryRemove(string connectionId, CancellationToken token);

    ValueTask<bool> AddToGroup(string group, string connectionId, CancellationToken token);
    ValueTask<bool> RemoveFromGroup(string group, string connectionId, CancellationToken token);

    ValueTask Send(WsTarget target, WsMessage envelope, CancellationToken ct = default);
}

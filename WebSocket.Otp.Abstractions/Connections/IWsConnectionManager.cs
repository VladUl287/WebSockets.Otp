using WebSockets.Otp.Abstractions.Options;

namespace WebSockets.Otp.Abstractions.Connections;

public interface IWsConnectionManager
{
    ValueTask<bool> TryAdd(IWsConnection connection, CancellationToken token);
    ValueTask<bool> TryRemove(string connectionId, CancellationToken token);

    ValueTask<bool> AddToGroup(string group, string connectionId, CancellationToken token);
    ValueTask<bool> RemoveFromGroup(string group, string connectionId, CancellationToken token);

    ValueTask Send(WsMessage envelope, CancellationToken token);
    ValueTask Send(string connectionId, WsMessage envelope, CancellationToken token);
    ValueTask Send(IEnumerable<string> connections, WsMessage envelope, CancellationToken token);

    ValueTask SendToGroup(string group, WsMessage envelope, CancellationToken token);
    ValueTask SendToGroup(IEnumerable<string> groups, WsMessage envelope, CancellationToken token);
}

using WebSockets.Otp.Abstractions.Connection;

namespace WebSockets.Otp.Abstractions.Utils.Managers;

public readonly struct GroupManager(IWsConnectionManager manager)
{
    public readonly ValueTask<bool> AddAsync(string group, string connectionId, CancellationToken token = default) =>
        manager.AddToGroup(group, connectionId, token);

    public readonly ValueTask<bool> RemoveAsync(string group, string connectionId, CancellationToken token = default) =>
        manager.RemoveFromGroup(group, connectionId, token);
}

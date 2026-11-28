using WebSockets.Otp.Abstractions.Connection;

namespace WebSockets.Otp.Abstractions.Utils.Managers;

public sealed class SendManager(IWsConnectionManager manager)
{
    private readonly IWsConnectionManager _manager = manager;
    private List<string>? _connectionsIds;
    private List<string>? _groups;
    private List<string>? _exclude;
    private bool _targetAll = false;

    private List<string> ConnectionsIds => (_connectionsIds ??= []);
    private List<string> Groups => (_groups ??= []);
    private List<string> Exclude => (_exclude ??= []);

    public SendManager Client(string connectionId)
    {
        if (_targetAll) return this;
        ConnectionsIds.Add(connectionId);
        return this;
    }

    public SendManager Clients(params string[] connectionsIds)
    {
        if (_targetAll) return this;
        ConnectionsIds.AddRange(connectionsIds);
        return this;
    }

    public SendManager Client(IReadOnlyList<string> connectionsIds)
    {
        if (_targetAll) return this;
        ConnectionsIds.AddRange(connectionsIds);
        return this;
    }

    public SendManager Except(string connectionId)
    {
        Exclude.Add(connectionId);
        return this;
    }

    public SendManager Except(params string[] connectionsIds)
    {
        Exclude.AddRange(connectionsIds);
        return this;
    }

    public SendManager Except(IReadOnlyList<string> connectionsIds)
    {
        Exclude.AddRange(connectionsIds);
        return this;
    }

    public SendManager Group(string groupName)
    {
        if (_targetAll) return this;
        Groups.Add(groupName);
        return this;
    }

    public SendManager Group(params string[] groups)
    {
        if (_targetAll) return this;
        Groups.AddRange(groups);
        return this;
    }

    public SendManager Group(IReadOnlyList<string> groups)
    {
        if (_targetAll) return this;
        Groups.AddRange(groups);
        return this;
    }

    public SendManager All()
    {
        _targetAll = true;
        return this;
    }

    public async ValueTask SendAsync<TData>(string key, TData data, CancellationToken token = default)
        where TData : notnull
    {
        try
        {
            if (!_targetAll && _connectionsIds is not { Count: > 0 } && _groups is not { Count: > 0 })
                return;

            var envelope = new EventMessage(key, data);

            var target = (_targetAll, _connectionsIds, _groups, _exclude) switch
            {
                (true, _, _, _) => 
                    WsTarget.Broadcast(_exclude),
                (_, { Count: 1 }, null or { Count: 0 }, null or { Count: 0 }) =>  
                    WsTarget.ToConnection(_connectionsIds[0]),
                (_, null or { Count: 0 }, { Count: 1 }, _) => 
                    WsTarget.ToGroup(_groups[0], _exclude),
                _ =>
                    WsTarget.ToMany(_connectionsIds, _groups, _exclude)
            };

            await _manager.Send(target, envelope, token);
        }
        finally
        {
            _connectionsIds?.Clear();
            _groups?.Clear();
            _exclude?.Clear();
            _targetAll = false;
        }
    }
}

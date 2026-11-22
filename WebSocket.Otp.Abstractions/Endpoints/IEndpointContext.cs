using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Abstractions.Utils.Managers;

namespace WebSockets.Otp.Abstractions.Endpoints;

public interface IEndpointContext : IGlobalContext
{
    IServiceProvider Services { get; }
    WsEndpointInfo Endpoint { get; }
    CancellationToken Token { get; }

    MessageHeaders MessageHeaders { get; }
    ReadOnlyMemory<byte> Message { get; }

    SendManager Send { get; }
    GroupManager Group { get; }
}

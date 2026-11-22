using System.Net.WebSockets;
using WebSockets.Otp.Abstractions.Serializers;

namespace WebSockets.Otp.Abstractions.Connection;

public interface IWsConnection
{
    string Id { get; }
    WebSocket Socket { get; }
    IMessageSerializer Serializer { get; }
}

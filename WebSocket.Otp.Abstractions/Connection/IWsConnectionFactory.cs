using System.Net.WebSockets;
using WebSockets.Otp.Abstractions.Serializers;

namespace WebSockets.Otp.Abstractions.Connection;

public interface IWsConnectionFactory
{
    IWsConnection Create(WebSocket socket, IMessageSerializer serializer);
}

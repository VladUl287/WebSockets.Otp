using System.Net.WebSockets;
using WebSockets.Otp.Abstractions.Options;

namespace WebSockets.Otp.Abstractions.Transport;

public interface IMessageEnumerator
{
    IAsyncEnumerable<IMessageBuffer> EnumerateAsync(WebSocket socket, WsOptions config, CancellationToken token);
    void ReturnBuffer(IMessageBuffer buffer);
}

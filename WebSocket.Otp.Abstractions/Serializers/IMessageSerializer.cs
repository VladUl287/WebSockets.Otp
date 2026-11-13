using System.Buffers;
using System.Net.WebSockets;
using WebSockets.Otp.Abstractions.Options;

namespace WebSockets.Otp.Abstractions.Serializers;

public interface IMessageSerializer
{
    string Protocol { get; }
    WebSocketMessageType Type { get; }

    MessageHeaders ReadHeaders(ReadOnlyMemory<byte> message);
    void SerializeMessage(WsMessage data, IBufferWriter<byte> buffer);
    T? Deserialize<T>(ReadOnlyMemory<byte> data);
}

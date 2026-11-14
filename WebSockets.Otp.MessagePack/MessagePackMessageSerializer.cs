using MessagePack;
using System.Buffers;
using System.Net.WebSockets;
using WebSockets.Otp.Abstractions.Options;
using WebSockets.Otp.Abstractions.Serializers;

namespace WebSockets.Otp.MessagePack;

public sealed class MessagePackMessageSerializer(MessagePackSerializerOptions options) : IMessageSerializer
{
    public string Protocol => "messagepack";
    public WebSocketMessageType Type => WebSocketMessageType.Binary;

    public T? Deserialize<T>(ReadOnlyMemory<byte> data) => MessagePackSerializer.Deserialize<T>(data, options);

    public void SerializeMessage(WsMessage envelope, IBufferWriter<byte> buffer)
    {
        var writer = new MessagePackWriter(buffer);

        writer.WriteArrayHeader(3);
        writer.Write(envelope.Type.Value);

        switch (envelope)
        {
            case ResponseMessage m:
                writer.Write(m.Id);
                MessagePackSerializer.Serialize(ref writer, m.Value, options);
                break;
            case ErrorMessage m:
                writer.Write(m.Id);
                writer.Write(m.Error);
                break;
            case EventMessage m:
                writer.Write(m.Key);
                MessagePackSerializer.Serialize(ref writer, m.Value, options);
                break;
            default:
                throw new InvalidOperationException($"Unknown message type: {envelope.GetType()}");
        }

        writer.Flush();
    }

    public MessageHeaders ReadHeaders(ReadOnlyMemory<byte> data)
    {
        var reader = new MessagePackReader(data);

        ReadOnlyMemory<byte> key;

        var count = reader.ReadArrayHeader();
        if (count != 3)
            throw new FormatException($"Headers array must have 3 elements, got {count}.");

        if (reader.NextMessagePackType != MessagePackType.Integer)
            throw new FormatException("Header 'id' must be an integer.");
        var id = reader.ReadUInt32();

        if (reader.NextMessagePackType != MessagePackType.String)
            throw new FormatException("Header 'key' must be a string.");

        if (reader.TryReadStringSpan(out ReadOnlySpan<byte> keySpan))
        {
            var end = (int)reader.Consumed;
            var start = end - keySpan.Length;
            key = data.Slice(start, keySpan.Length);
        }
        else
        {
            var seq = reader.ReadStringSequence() ?? throw new FormatException();
            key = seq.IsSingleSegment ? seq.First : seq.ToArray();
        }

        var valStart = (int)reader.Consumed;
        reader.Skip();
        var valEnd = (int)reader.Consumed;
        var value = data[valStart..valEnd];

        return new(id, key, value);
    }
}

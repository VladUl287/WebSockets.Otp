using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using WebSockets.Otp.Abstractions.Options;
using WebSockets.Otp.Abstractions.Serializers;

namespace WebSockets.Otp.Core.Services.Serializers;

public sealed class JsonMessageSerializer(JsonSerializerOptions options) : IMessageSerializer
{
    public string Protocol => "json";
    public WebSocketMessageType Type => WebSocketMessageType.Text;

    public T? Deserialize<T>(ReadOnlyMemory<byte> data) => JsonSerializer.Deserialize<T>(data.Span, options);

    private static readonly JsonWriterOptions WriterOptions = new() { SkipValidation = true };
    public void SerializeMessage(WsMessage data, IBufferWriter<byte> buffer)
    {
        using var writer = new Utf8JsonWriter(buffer, WriterOptions);

        writer.WriteStartObject();
        writer.WriteNumber("type", data.Type.Value);

        data.WritePayload(writer, options);

        writer.WriteEndObject();
        writer.Flush();
    }

    public MessageHeaders ReadHeaders(ReadOnlyMemory<byte> data)
    {
        var dataSpan = data.Span;
        var reader = new Utf8JsonReader(dataSpan);

        uint id = 0u;
        ReadOnlyMemory<byte> key = default;
        ReadOnlyMemory<byte> value = default;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    if (reader.CurrentDepth != 1)
                        break;

                    if (reader.ValueTextEquals("key"))
                    {
                        reader.Read();
                        var start = reader.TokenStartIndex;
                        reader.Skip();
                        var end = reader.BytesConsumed;
                        key = data[((int)start + 1)..((int)end - 1)];
                    }
                    else if (reader.ValueTextEquals("id"))
                    {
                        reader.Read();
                        var start = reader.TokenStartIndex;
                        reader.Skip();
                        var end = reader.BytesConsumed;
                        id = uint.Parse(dataSpan[(int)start..(int)end]);
                    }
                    else if (reader.ValueTextEquals("value"))
                    {
                        reader.Read();
                        var start = reader.TokenStartIndex;
                        reader.Skip();
                        var end = reader.BytesConsumed;
                        value = data[(int)start..(int)end];
                    }
                    break;
            }
        }

        return new(id, key, value);
    }
}
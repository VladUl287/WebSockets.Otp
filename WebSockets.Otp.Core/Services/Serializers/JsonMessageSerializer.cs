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
            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
                continue;

            if (reader.ValueTextEquals("id"u8))
            {
                reader.Read();

                if (reader.TokenType != JsonTokenType.Number)
                    throw new FormatException("Header 'id' must be a number.");

                id = reader.GetUInt32();
            }
            else if (reader.ValueTextEquals("key"u8))
            {
                reader.Read();

                var start = reader.TokenStartIndex;

                if (reader.TokenType != JsonTokenType.String)
                    throw new FormatException("Header 'key' must be a string.");

                reader.Skip();
                var end = reader.BytesConsumed;

                key = data[((int)start + 1)..((int)end - 1)];
            }
            else if (reader.ValueTextEquals("value"u8))
            {
                var start = (int)reader.TokenStartIndex;

                reader.Skip();

                value = data[start..(int)reader.BytesConsumed];
            }
            else
            {
                reader.Skip();
            }
        }

        return new(id, key, value);
    }
}
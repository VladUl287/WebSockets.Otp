using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WebSockets.Otp.Abstractions.Connection;
using WebSockets.Otp.Core.Services.Serializers;

namespace WebSockets.Otp.Core.Tests.Serializers;

public sealed class JsonMessageSerializerTests
{
    private static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web);

    private static JsonMessageSerializer NewSerializer(JsonSerializerOptions? options = null)
        => new(options ?? DefaultOptions);

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
    private static string Str(ReadOnlyMemory<byte> m) => Encoding.UTF8.GetString(m.Span);

    private static ResponseMessage Response(uint id, object? value)
        => new(id, value);

    private static ErrorMessage Error(uint id, WsErrorCode errorCode, string error)
        => new(id, errorCode, error);

    private static EventMessage Event(string key, object? value)
        => new(key, value);

    [Fact]
    public void Protocol_IsJson() => Assert.Equal("json", NewSerializer().Protocol);

    [Fact]
    public void Type_IsText() => Assert.Equal(WebSocketMessageType.Text, NewSerializer().Type);

    public sealed class SimpleDto
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    [Fact]
    public void Deserialize_RoundTrips_Object()
    {
        var s = NewSerializer();
        var bytes = Utf8("""{"name":"x","age":7}""");

        var dto = s.Deserialize<SimpleDto>(bytes);

        Assert.NotNull(dto);
        Assert.Equal("x", dto!.Name);
        Assert.Equal(7, dto.Age);
    }

    [Fact]
    public void Deserialize_UsesProvidedOptions()
    {
        var ci = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var s = NewSerializer(ci);

        var dto = s.Deserialize<SimpleDto>(Utf8("""{"NAME":"x","AGE":9}"""));

        Assert.Equal("x", dto!.Name);
        Assert.Equal(9, dto.Age);
    }

    [Fact]
    public void Deserialize_MalformedJson_Throws() =>
        Assert.ThrowsAny<JsonException>(() => NewSerializer().Deserialize<SimpleDto>(Utf8("{x")));

    [Fact]
    public void Serialize_ResponseMessage_WritesType_Id_Value()
    {
        var buffer = new ArrayBufferWriter<byte>();
        NewSerializer().SerializeMessage(Response(42, new { x = 1 }), buffer);

        using var doc = JsonDocument.Parse(buffer.WrittenMemory);
        var root = doc.RootElement;

        Assert.Equal(42u, root.GetProperty("id").GetUInt32());
        Assert.Equal(1, root.GetProperty("value").GetProperty("x").GetInt32());
        Assert.True(root.TryGetProperty("type", out _));
    }

    [Fact]
    public void Serialize_ErrorMessage_WritesType_Id_Error()
    {
        var buffer = new ArrayBufferWriter<byte>();
        NewSerializer().SerializeMessage(Error(7, WsErrorCode.Unauthorized, "nope"), buffer);

        using var doc = JsonDocument.Parse(buffer.WrittenMemory);
        var root = doc.RootElement;

        Assert.Equal(7u, root.GetProperty("id").GetUInt32());
        Assert.Equal(3, root.GetProperty("code").GetInt32());
        Assert.Equal("nope", root.GetProperty("message").GetString());
    }

    [Fact]
    public void Serialize_EventMessage_WritesType_Key_Value()
    {
        var buffer = new ArrayBufferWriter<byte>();
        NewSerializer().SerializeMessage(Event("evt", new { y = 2 }), buffer);

        using var doc = JsonDocument.Parse(buffer.WrittenMemory);
        var root = doc.RootElement;

        Assert.Equal("evt", root.GetProperty("key").GetString());
        Assert.Equal(2, root.GetProperty("value").GetProperty("y").GetInt32());
    }

    [Fact]
    public void Serialize_UsesProvidedOptions_ForNestedValues()
    {
        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var buffer = new ArrayBufferWriter<byte>();

        NewSerializer(camel).SerializeMessage(Response(1, new { PascalName = "v" }), buffer);

        Assert.Contains("\"pascalName\"", Str(buffer.WrittenMemory));
    }

    [Fact]
    public void Serialize_ProducesValidJson_ForAllMessageTypes()
    {
        var s = NewSerializer();

        foreach (var (msg, label) in new (WsMessage, string)[]
        {
            (Response(1, new { }), "response"),
            (Error(1, WsErrorCode.Unauthorized, "e"), "error"),
            (Event("k", 1), "event")
        })
        {
            var buffer = new ArrayBufferWriter<byte>();
            var ex = Record.Exception(() => s.SerializeMessage(msg, buffer));
            Assert.Null(ex);
            Assert.NotEmpty(buffer.WrittenSpan.ToArray());

            using var _ = JsonDocument.Parse(buffer.WrittenMemory);
        }
    }

    [Fact]
    public void ReadHeaders_EmptyObject_ReturnsDefaults()
    {
        var h = NewSerializer().ReadHeaders(Utf8("{}"));

        Assert.Equal(0u, h.CorrelationId);
        Assert.True(h.Key.IsEmpty);
        Assert.True(h.Value.IsEmpty);
    }

    [Fact]
    public void ReadHeaders_IdOnly()
    {
        var h = NewSerializer().ReadHeaders(Utf8("""{"type":1,"id":42}"""));

        Assert.Equal(42u, h.CorrelationId);
        Assert.True(h.Key.IsEmpty);
        Assert.True(h.Value.IsEmpty);
    }

    [Fact]
    public void ReadHeaders_KeyOnly()
    {
        var h = NewSerializer().ReadHeaders(Utf8("""{"type":1,"key":"abc"}"""));

        Assert.Equal(0u, h.CorrelationId);
        Assert.Equal("abc", Str(h.Key));
        Assert.True(h.Value.IsEmpty);
    }

    [Fact]
    public void ReadHeaders_ValueOnly_ObjectCapture()
    {
        var h = NewSerializer().ReadHeaders(Utf8("""{"type":1,"value":{"x":1}}"""));

        Assert.Equal(0u, h.CorrelationId);
        Assert.True(h.Key.IsEmpty);
        Assert.Equal("""{"x":1}""", Str(h.Value));
    }

    [Fact]
    public void ReadHeaders_ValueOnly_ArrayCapture()
    {
        var h = NewSerializer().ReadHeaders(Utf8("""{"type":1,"value":[1,2,3]}"""));

        Assert.Equal("[1,2,3]", Str(h.Value));
    }

    [Fact]
    public void ReadHeaders_ValueOnly_PrimitiveCapture()
    {
        var h = NewSerializer().ReadHeaders(Utf8("""{"type":1,"value":42}"""));

        Assert.Equal("42", Str(h.Value));
    }

    [Fact]
    public void ReadHeaders_AllFields()
    {
        var h = NewSerializer().ReadHeaders(
            Utf8("""{"type":1,"id":7,"key":"k","value":[1,2,3]}"""));

        Assert.Equal(7u, h.CorrelationId);
        Assert.Equal("k", Str(h.Key));
        Assert.Equal("[1,2,3]", Str(h.Value));
    }

    [Fact]
    public void ReadHeaders_IsOrderIndependent()
    {
        var h = NewSerializer().ReadHeaders(
            Utf8("""{"value":true,"key":"k","id":1,"type":1}"""));

        Assert.Equal(1u, h.CorrelationId);
        Assert.Equal("k", Str(h.Key));
        Assert.Equal("true", Str(h.Value));
    }

    [Fact]
    public void ReadHeaders_WhitespaceInJson_IsIgnored()
    {
        var h = NewSerializer().ReadHeaders(
            Utf8("  {\n  \"id\" : 5 ,\n  \"key\" : \"k\"\n}  "));

        Assert.Equal(5u, h.CorrelationId);
        Assert.Equal("k", Str(h.Key));
    }

    [Fact]
    public void ReadHeaders_UnknownProperties_AreIgnored()
    {
        var h = NewSerializer().ReadHeaders(
            Utf8("""{"type":1,"foo":"bar","id":5,"baz":[1,2]}"""));

        Assert.Equal(5u, h.CorrelationId);
    }

    [Fact]
    public void ReadHeaders_NestedId_NotRead()
    {
        var h = NewSerializer().ReadHeaders(
            Utf8("""{"type":1,"value":{"id":99},"id":7}"""));

        Assert.Equal(7u, h.CorrelationId);
        Assert.Equal("""{"id":99}""", Str(h.Value));
    }

    [Fact]
    public void ReadHeaders_DuplicateKey_LastWins()
    {
        var h = NewSerializer().ReadHeaders(Utf8("""{"key":"a","key":"b"}"""));

        Assert.Equal("b", Str(h.Key));
    }

    [Fact]
    public void ReadHeaders_DoesNotThrow_ForSimpleKeyValue()
    {
        var ex = Record.Exception(() => NewSerializer().ReadHeaders(
            Utf8("""{"type":1,"key":"abc","value":42}""")));
        Assert.Null(ex);
    }

    [Fact]
    public void ReadHeaders_IdNotNumber_Throws() =>
        Assert.Throws<FormatException>(() =>
            NewSerializer().ReadHeaders(Utf8("""{"id":"42"}""")));

    [Fact]
    public void ReadHeaders_KeyNotString_Throws() =>
        Assert.Throws<FormatException>(() =>
            NewSerializer().ReadHeaders(Utf8("""{"key":42}""")));

    [Fact]
    public void ReadHeaders_IdExceedsUInt32_Throws() =>
        Assert.Throws<FormatException>(() =>
            NewSerializer().ReadHeaders(Utf8("""{"id":4294967296}""")));

    [Fact]
    public void ReadHeaders_IdNegative_Throws() =>
        Assert.Throws<FormatException>(() =>
            NewSerializer().ReadHeaders(Utf8("""{"id":-1}""")));

    [Fact]
    public void ReadHeaders_IdNonInteger_Throws() =>
        Assert.Throws<FormatException>(() =>
            NewSerializer().ReadHeaders(Utf8("""{"id":1.5}""")));

    [Fact]
    public void ReadHeaders_InvalidJson_Throws() =>
        Assert.ThrowsAny<JsonException>(() =>
            NewSerializer().ReadHeaders(Utf8("{not json")));

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(uint.MaxValue)]
    public void RoundTrip_ResponseMessage_HeadersMatchBody(uint id)
    {
        var s = NewSerializer();
        var buffer = new ArrayBufferWriter<byte>();
        s.SerializeMessage(Response(id, new { z = 3 }), buffer);

        var h = s.ReadHeaders(buffer.WrittenMemory);

        Assert.Equal(id, h.CorrelationId);
        Assert.Equal("""{"z":3}""", Str(h.Value));
    }

    [Fact]
    public void RoundTrip_ErrorMessage_HeadersMatchBody()
    {
        var s = NewSerializer();
        var buffer = new ArrayBufferWriter<byte>();
        s.SerializeMessage(Error(11, WsErrorCode.Unauthorized, "bad"), buffer);

        var h = s.ReadHeaders(buffer.WrittenMemory);

        Assert.Equal(11u, h.CorrelationId);
    }

    [Fact]
    public void RoundTrip_EventMessage_HeadersMatchBody()
    {
        var s = NewSerializer();
        var buffer = new ArrayBufferWriter<byte>();
        s.SerializeMessage(Event("topic", new[] { 1, 2 }), buffer);

        var h = s.ReadHeaders(buffer.WrittenMemory);

        Assert.Equal("topic", Str(h.Key));
        Assert.Equal("[1,2]", Str(h.Value));
    }

    [Fact]
    public void ReadHeaders_KeyWithEscapedNewline_ReturnsDecodedBytes()
    {
        var h = NewSerializer().ReadHeaders(Utf8("""{"key":"a\nb"}"""));

        Assert.Equal("a\\nb", Str(h.Key));
    }

    [Fact]
    public void ReadHeaders_HonoursCommentHandlingFromOptions()
    {
        var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip };
        var s = NewSerializer(options);

        var h = s.ReadHeaders(Utf8("""{"type":1,"id":5}"""));

        Assert.Equal(5u, h.CorrelationId);
    }
}

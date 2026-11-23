using MessagePack;
using MessagePack.Resolvers;
using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using WebSockets.Otp.Abstractions.Connection;

namespace WebSockets.Otp.MessagePack.Tests;

public sealed class MessagePackMessageSerializerTests
{
    private static readonly MessagePackSerializerOptions DefaultOptions =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    private static MessagePackMessageSerializer NewSerializer(MessagePackSerializerOptions? options = null)
        => new(options ?? DefaultOptions);

    private static string Str(ReadOnlyMemory<byte> m) => Encoding.UTF8.GetString(m.Span);

    private static ResponseMessage Response(uint id, object? value)
        => new(id, value);

    private static ErrorMessage Error(uint id, WsErrorCode code, string error)
        => new(id, code, error);

    private static EventMessage Event(string key, object? value)
        => new(key, value);

    private delegate void ValueWriter(ref MessagePackWriter writer);

    private static byte[] BuildHeaders(uint id, string key, ValueWriter writeValue)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(3);
        writer.Write(id);
        writer.Write(key);
        writeValue(ref writer);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    [Fact] public void Protocol_IsMessagePack() => Assert.Equal("messagepack", NewSerializer().Protocol);
    [Fact] public void Type_IsBinary() => Assert.Equal(WebSocketMessageType.Binary, NewSerializer().Type);

    public sealed class SimpleDto
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    [Fact]
    public void Deserialize_RoundTrips_Object()
    {
        var s = NewSerializer();
        var bytes = MessagePackSerializer.Serialize(new SimpleDto { Name = "x", Age = 7 }, DefaultOptions);

        var dto = s.Deserialize<SimpleDto>(bytes);

        Assert.NotNull(dto);
        Assert.Equal("x", dto!.Name);
        Assert.Equal(7, dto.Age);
    }

    [Fact]
    public void Deserialize_Malformed_Throws() =>
        Assert.ThrowsAny<Exception>(() => NewSerializer().Deserialize<SimpleDto>(new byte[] { 0xC1 }));

    [Fact]
    public void Serialize_ResponseMessage_WritesArray3_Type_Id_Value()
    {
        var buffer = new ArrayBufferWriter<byte>();
        NewSerializer().SerializeMessage(Response(42, 100), buffer);

        var reader = new MessagePackReader(buffer.WrittenMemory);
        Assert.Equal(3, reader.ReadArrayHeader());
        Assert.Equal(Convert.ToUInt32(MessageType.Response.Value), reader.ReadUInt32());
        Assert.Equal(42u, reader.ReadUInt32());
        Assert.Equal(100, reader.ReadInt32());
    }

    [Fact]
    public void Serialize_ErrorMessage_WritesArray3_Type_Id_Error()
    {
        var buffer = new ArrayBufferWriter<byte>();
        NewSerializer().SerializeMessage(Error(7, WsErrorCode.Unauthorized, "nope"), buffer);

        var reader = new MessagePackReader(buffer.WrittenMemory);
        Assert.Equal(3, reader.ReadArrayHeader());
        Assert.Equal(Convert.ToUInt32(MessageType.Error.Value), reader.ReadUInt32());
        Assert.Equal(7u, reader.ReadUInt32());
        Assert.Equal(3, reader.ReadInt32());
        Assert.Equal("nope", reader.ReadString());
    }

    [Fact]
    public void Serialize_EventMessage_WritesArray3_Type_Key_Value()
    {
        var buffer = new ArrayBufferWriter<byte>();
        NewSerializer().SerializeMessage(Event("topic", 5), buffer);

        var reader = new MessagePackReader(buffer.WrittenMemory);
        Assert.Equal(3, reader.ReadArrayHeader());
        Assert.Equal(Convert.ToUInt32(MessageType.Event.Value), reader.ReadUInt32());
        Assert.Equal("topic", reader.ReadString());
        Assert.Equal(5, reader.ReadInt32());
    }

    [Fact]
    public void Serialize_AlwaysWritesThreeElements()
    {
        var types = new (WsMessage, string)[]
        {
            (Response(1, 1), "response"),
            (Error(1, WsErrorCode.Unauthorized, "e"), "error"),
            (Event("k", 1), "event"),
        };

        foreach (var (msg, _) in types)
        {
            var buffer = new ArrayBufferWriter<byte>();
            NewSerializer().SerializeMessage(msg, buffer);

            var reader = new MessagePackReader(buffer.WrittenMemory);
            Assert.Equal(3, reader.ReadArrayHeader());
        }
    }

    [Fact]
    public void Serialize_UnicodeKey_RoundTripsThroughRawBytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        NewSerializer().SerializeMessage(Event("héllo", 1), buffer);

        var reader = new MessagePackReader(buffer.WrittenMemory);
        reader.ReadArrayHeader();
        reader.ReadUInt32();
        Assert.Equal("héllo", reader.ReadString());
    }

    [Fact]
    public void Serialize_LargeId_RoundTrips()
    {
        var buffer = new ArrayBufferWriter<byte>();
        NewSerializer().SerializeMessage(Response(uint.MaxValue, 0), buffer);

        var reader = new MessagePackReader(buffer.WrittenMemory);
        reader.ReadArrayHeader();
        reader.ReadUInt32();
        Assert.Equal(uint.MaxValue, reader.ReadUInt32());
    }

    [Fact]
    public void ReadHeaders_AllFields()
    {
        var bytes = BuildHeaders(42, "abc", (ref MessagePackWriter w) => w.WriteInt32(7));
        var h = NewSerializer().ReadHeaders(bytes);

        Assert.Equal(42u, h.CorrelationId);
        Assert.Equal("abc", Str(h.Key));
        Assert.False(h.Value.IsEmpty);
    }

    [Fact]
    public void ReadHeaders_EmptyKey()
    {
        var h = NewSerializer().ReadHeaders(BuildHeaders(1, "", (ref MessagePackWriter w) => w.Write(0)));

        Assert.Equal(1u, h.CorrelationId);
        Assert.True(h.Key.IsEmpty);
    }

    [Fact]
    public void ReadHeaders_UnicodeKey_RawBytesAreUtf8()
    {
        var h = NewSerializer().ReadHeaders(BuildHeaders(1, "héllo", (ref MessagePackWriter w) => w.Write(0)));
        Assert.Equal("héllo", Str(h.Key));
    }

    [Fact]
    public void ReadHeaders_ValueAsMap_IsCapturedRaw()
    {
        var bytes = BuildHeaders(1, "k", (ref MessagePackWriter w) =>
        {
            w.WriteMapHeader(1);
            w.Write("x");
            w.Write(42);
        });

        var h = NewSerializer().ReadHeaders(bytes);

        Assert.Equal(1u, h.CorrelationId);
        Assert.Equal("k", Str(h.Key));

        var reader = new MessagePackReader(h.Value);
        Assert.Equal(1, reader.ReadMapHeader());
        Assert.Equal("x", reader.ReadString());
        Assert.Equal(42, reader.ReadInt32());
    }

    [Fact]
    public void ReadHeaders_ValueAsArray_IsCapturedRaw()
    {
        var h = NewSerializer().ReadHeaders(BuildHeaders(1, "k", (ref MessagePackWriter w) =>
        {
            w.WriteArrayHeader(2);
            w.Write(1); w.Write(2);
        }));

        var reader = new MessagePackReader(h.Value);
        Assert.Equal(2, reader.ReadArrayHeader());
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(2, reader.ReadInt32());
    }

    [Fact]
    public void ReadHeaders_ValueAsNil_IsEmptySlice()
    {
        var h = NewSerializer().ReadHeaders(BuildHeaders(1, "k", (ref MessagePackWriter w) => w.WriteNil()));
        Assert.False(h.Value.IsEmpty);
        Assert.Equal(0xC0, h.Value.Span[0]);
    }

    [Fact]
    public void ReadHeaders_LargeId()
    {
        var h = NewSerializer().ReadHeaders(BuildHeaders(uint.MaxValue, "k", (ref MessagePackWriter w) => w.Write(0)));
        Assert.Equal(uint.MaxValue, h.CorrelationId);
    }

    [Fact]
    public void ReadHeaders_WrongArrayCount_Throws()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new MessagePackWriter(buffer);
        w.WriteArrayHeader(4);
        w.Write(1u); w.Write("k"); w.Write(0); w.Write(0);
        w.Flush();

        var ex = Assert.Throws<FormatException>(() => NewSerializer().ReadHeaders(buffer.WrittenMemory));
        Assert.Contains("3 elements", ex.Message);
    }

    [Fact]
    public void ReadHeaders_NotAnArray_Throws() =>
        Assert.ThrowsAny<Exception>(() =>
            NewSerializer().ReadHeaders(new byte[] { 0x01 }));

    [Fact]
    public void ReadHeaders_NilInsteadOfArray_Throws() =>
        Assert.ThrowsAny<Exception>(() =>
            NewSerializer().ReadHeaders(new byte[] { 0xC0 }));

    [Fact]
    public void ReadHeaders_EmptyInput_Throws() =>
        Assert.ThrowsAny<Exception>(() =>
            NewSerializer().ReadHeaders(ReadOnlyMemory<byte>.Empty));

    [Fact]
    public void ReadHeaders_IdNotInteger_Throws()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new MessagePackWriter(buffer);
        w.WriteArrayHeader(3);
        w.Write("id");
        w.Write("k");
        w.Write(0);
        w.Flush();

        var ex = Assert.Throws<FormatException>(() => NewSerializer().ReadHeaders(buffer.WrittenMemory));
        Assert.Contains("'id'", ex.Message);
    }

    [Fact]
    public void ReadHeaders_KeyNotString_Throws()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new MessagePackWriter(buffer);
        w.WriteArrayHeader(3);
        w.Write(1u);
        w.Write(42);
        w.Write(0);
        w.Flush();

        var ex = Assert.Throws<FormatException>(() => NewSerializer().ReadHeaders(buffer.WrittenMemory));
        Assert.Contains("'key'", ex.Message);
    }
}

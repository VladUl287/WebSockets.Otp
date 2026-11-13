using Microsoft.Extensions.ObjectPool;
using System.Buffers;
using WebSockets.Otp.Abstractions.Options;

namespace WebSockets.Otp.Core.Services.Utils;

public sealed class BufferWriterPolicy(WsOptions options) : PooledObjectPolicy<ArrayBufferWriter<byte>>
{
    public override ArrayBufferWriter<byte> Create() => new(initialCapacity: 1024);

    public override bool Return(ArrayBufferWriter<byte> obj)
    {
        if (obj.Capacity > options.MaxBufferSize)
        {
            return false;
        }

        obj.ResetWrittenCount();
        return true;
    }
}

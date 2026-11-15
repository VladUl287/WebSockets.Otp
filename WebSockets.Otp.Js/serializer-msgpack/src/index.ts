import type { Serializer } from "@fefruess/websockets.otp.serializer.core"
import { decode, encode, type DecoderOptions, type EncoderOptions } from "@msgpack/msgpack"

const encoder = new TextEncoder()
export const asBytes = async (data: string | BufferSource | Blob): Promise<Uint8Array> => {
    if (typeof data === 'string')
        return encoder.encode(data)

    if (data instanceof ArrayBuffer)
        return new Uint8Array(data)

    if (ArrayBuffer.isView(data))
        return new Uint8Array(data.buffer)

    return new Uint8Array(await data.arrayBuffer())
}

export const msgPackSerializer = (options?: { encoder?: EncoderOptions<unknown>, decoder?: DecoderOptions<unknown> }): Serializer => ({
    protocol: 'messagepack',
    serialize: (data) => encode(data, options?.encoder),
    deserialize: async (data) => decode(await asBytes(data), options?.decoder),
})
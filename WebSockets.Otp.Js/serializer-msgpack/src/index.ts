import { MessageType, type MessageSerializer } from "@fefruess/websockets.otp.serializer.core"
import { Decoder, Encoder, type DecoderOptions, type EncoderOptions } from "@msgpack/msgpack"

const encoder = new TextEncoder()
export const toBytes = async (data: string | BufferSource | Blob): Promise<Uint8Array> => {
    if (typeof data === 'string')
        return encoder.encode(data)

    if (data instanceof ArrayBuffer)
        return new Uint8Array(data)

    if (ArrayBuffer.isView(data))
        return new Uint8Array(data.buffer)

    return new Uint8Array(await data.arrayBuffer())
}

export const msgPackSerializer = (options?: {
    encoder?: EncoderOptions<unknown>
    decoder?: DecoderOptions<unknown>
}): MessageSerializer => {
    const encoder = new Encoder(options?.encoder)
    const decoder = new Decoder(options?.decoder)

    return {
        protocol: 'messagepack',
        serialize: (data) => encoder.encode(data),
        deserialize: async (data) => decoder.decode(await toBytes(data)),
        serializeMessage: (data) => {
            if (data === null) throw new TypeError('data is null/undefined')

            if (!Number.isInteger(data.id) || data.id < 0 || data.id > 0xffffffff)
                throw new TypeError(`id must be uint32, got ${data.id}`)

            if (typeof data.key !== 'string')
                throw new TypeError(`key must be a string, got ${typeof data.key}`)

            return encoder.encode([data.id, data.key, data.value])
        },
        deserializeMessage: async (bytes) => {
            const [type, a, b] = decoder.decode(await toBytes(bytes)) as [number, unknown, unknown]

            switch (type) {
                case MessageType.response:
                    return { type, id: a as number, value: b }
                case MessageType.error:
                    return { type, id: a as number, error: b as string }
                case MessageType.event:
                    return { type, key: a as string, value: b }
                default:
                    throw new Error(`Unknown message type: ${type}`);
            }
        }
    }
}
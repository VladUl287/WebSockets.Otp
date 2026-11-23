import { MessageType, WsErrorCode, type MessageSerializer } from "@fefruess/websockets.otp.serializer.core"
import { Decoder, Encoder, type DecoderOptions, type EncoderOptions } from "@msgpack/msgpack"

const textEncoder = new TextEncoder()

export const msgPackSerializer = (options?: {
    encoder?: EncoderOptions<unknown>
    decoder?: DecoderOptions<unknown>
}): MessageSerializer => {
    const encoder = new Encoder(options?.encoder)
    const decoder = new Decoder(options?.decoder)

    return {
        protocol: 'messagepack',

        serialize: (data) => encoder.encode(data),
        deserialize: (data) => {
            return decoder.decode(typeof data === "string" ? textEncoder.encode(data) : data)
        },

        serializeRequest: (data) => {
            if (data === null) throw new TypeError('data is null/undefined')

            if (!Number.isInteger(data.id) || data.id < 0 || data.id > 0xffffffff)
                throw new TypeError(`id must be uint32, got ${data.id}`)

            if (typeof data.key !== 'string')
                throw new TypeError(`key must be a string, got ${typeof data.key}`)

            return encoder.encode([data.id, data.key, data.value])
        },
        deserializeServer: (data) => {
            const [type, a, b, c] = decoder.decode(typeof data === "string" ? textEncoder.encode(data) : data) as [number, unknown, unknown, unknown]

            switch (type) {
                case MessageType.response:
                    return { type, id: a as number, value: b }
                case MessageType.error:
                    return { type, id: a as number, code: b as WsErrorCode, message: c as string }
                case MessageType.event:
                    return { type, key: a as string, value: b }
                default:
                    throw new Error(`Unknown message type: ${type}`);
            }
        }
    }
}
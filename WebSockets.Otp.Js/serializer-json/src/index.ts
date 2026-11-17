import { MessageType, type MessageSerializer } from "@fefruess/websockets.otp.serializer.core"

const encoder = new TextEncoder()
const decoder = new TextDecoder()

export const toBytes = async (data: BufferSource | Blob): Promise<Uint8Array> => {
    if (data instanceof ArrayBuffer)
        return new Uint8Array(data)

    if (ArrayBuffer.isView(data))
        return new Uint8Array(data.buffer)

    return new Uint8Array(await data.arrayBuffer())
}

export const jsonSerializer = (): MessageSerializer => ({
    protocol: 'json',
    serialize: (data) => JSON.stringify(data),
    deserialize: async (data) => JSON.parse(typeof data === "string" ? data : decoder.decode(await toBytes(data))),
    serializeMessage: (data) => {
        return `{"id":${JSON.stringify(data.id)},"key":${JSON.stringify(data.key)},"value":${JSON.stringify(data.value)}}`
    },
    deserializeMessage: async (data) => {
        const result = JSON.parse(typeof data === "string" ? data : decoder.decode(await toBytes(data)))
        switch (result.type) {
            case MessageType.response:
            case MessageType.error:
            case MessageType.event:
                return result
            default:
                throw new Error(`Unknown message type: ${result.type}`);
        }
    }
})
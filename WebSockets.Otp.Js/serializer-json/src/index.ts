import { MessageType, type MessageSerializer, type ServerMessage } from "@fefruess/websockets.otp.serializer.core"

const decoder = new TextDecoder()

function isServerMessage(v: unknown): v is ServerMessage {
    if (typeof v !== 'object' || v === null) return false

    const o = v as Record<string, unknown>
    switch (o.type) {
        case MessageType.response: return typeof o.id === 'number'
        case MessageType.error: return typeof o.id === 'number' && typeof o.code === 'number' && typeof o.message === 'string'
        case MessageType.event: return typeof o.key === 'string'
        default: return false
    }
}

export const jsonSerializer = (): MessageSerializer => {
    return {
        protocol: 'json',

        serialize: (data) => JSON.stringify(data),
        deserialize: (data) => JSON.parse(typeof data === "string" ? data : decoder.decode(data)),

        serializeRequest: (data) => {
            return `{"id":${data.id},"key":"${data.key}","value":${JSON.stringify(data.value)}}`
        },
        deserializeServer: (data) => {
            const result = JSON.parse(typeof data === "string" ? data : decoder.decode(data))
            if (!isServerMessage(result)) {
                throw new Error(`invalid server message: json`)
            }
            return result
        }
    }
}
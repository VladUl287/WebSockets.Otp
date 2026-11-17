export const MessageType = { response: 1, error: 2, event: 3 } as const
export type MessageType = typeof MessageType[keyof typeof MessageType]

export type ServerMessage =
    | { type: typeof MessageType.response; id: number; value: unknown }
    | { type: typeof MessageType.error; id: number; error: string }
    | { type: typeof MessageType.event; key: string; value: unknown }

export type RequestMessage<T> = { id: number; key: string; value: T }

export type Encoded = string | ArrayBuffer | Uint8Array<ArrayBuffer>

export interface Serializer {
    readonly protocol: string
    serialize(data: unknown): Encoded
    deserialize(data: Encoded): unknown
}

export interface MessageSerializer extends Serializer {
    serializeRequest<T>(data: RequestMessage<T>): Encoded
    deserializeServer(data: Encoded): ServerMessage
}
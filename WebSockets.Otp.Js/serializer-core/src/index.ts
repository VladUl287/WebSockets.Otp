export enum MessageType {
    response = 1,
    error = 2,
    event = 3
}

export type ServerMessage =
    | { type: MessageType.response; id: number; value: unknown }
    | { type: MessageType.error; id: number; error: string }
    | { type: MessageType.event; key: string; value: unknown }

export type RequestMessage<T> = { id: number; key: string; value: T }

export interface MessageSerializer {
    readonly protocol: string
    readonly serialize: (data: unknown) => BufferSource | Blob | string
    readonly deserialize: (data: string | BufferSource | Blob) => Promise<unknown>
    readonly serializeMessage: (data: RequestMessage<unknown>) => BufferSource | Blob | string
    readonly deserializeMessage: (data: string | BufferSource | Blob) => Promise<ServerMessage>
}
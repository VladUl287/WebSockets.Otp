export enum MessageType {
    response = 1,
    error = 2,
    event = 3
}

export type ResponseMessage = {
    type: MessageType.response
    id: number
    value: unknown
}

export type ErrorMessage = {
    type: MessageType.error
    id: number
    error: string
}

export type EventMessage = {
    type: MessageType.event
    key: string
    value: unknown
}

export type WsMessage = ResponseMessage | ErrorMessage | EventMessage

export type WsMessageRequest<T> = {
    id: number,
    key: string,
    value: T
}

export interface HandshakeMessage {
    protocol: string
}

export interface EndpointMessage {
    key: string
    correlationId?: number,
    [field: string]: unknown
}

export type ConnectionState =
    | "disconnected"
    | "connecting"
    | "connected"

export interface PendingRequest {
    readonly resolve: (value: any) => void
    readonly reject: (reason?: unknown) => void
}
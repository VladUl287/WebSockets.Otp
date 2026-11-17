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
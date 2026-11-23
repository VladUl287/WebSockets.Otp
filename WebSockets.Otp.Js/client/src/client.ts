import { jsonSerializer } from "@fefruess/websockets.otp.json"
import { MessageType, type Encoded, type MessageSerializer, type ServerMessage } from "@fefruess/websockets.otp.serializer.core"
import { usePendingRegistry, type PendingRegistry } from "./pending.js"
import { buildSchedule } from "./schedule.js"
import { type ConnectionState } from "./types.js"

const defaultFactory = (url: string): WebSocket => {
    if (typeof WebSocket === "undefined") {
        throw new Error("No global WebSocket. Pass `webSocketFactory`")
    }
    return new WebSocket(url)
}

type Handler<T> = (value: T) => void

export type WsClientOptions = {
    readonly url: string | (() => string),
    readonly defaultSerializer?: MessageSerializer,
    readonly serializer?: MessageSerializer,
    readonly socketFactory?: typeof defaultFactory,
    readonly scheduleFactory?: ReturnType<typeof buildSchedule>,
    readonly pendingFactory?: () => PendingRegistry<number>,
    readonly handshakeTimeoutMs?: number,
}

export type WsClient = {
    readonly state: () => ConnectionState
    readonly connect: () => Promise<void>
    readonly close: (code?: number, reason?: string) => void
    readonly send: <TRequest, TResponse>(key: string, payload: TRequest) => Promise<TResponse>
    readonly notify: <TRequest>(key: string, payload: TRequest) => void
    readonly receive: <TValue>(key: string, callback: Handler<TValue>) => (() => void)
}

export const useWsClient = (options: WsClientOptions): WsClient => {
    const {
        url,
        defaultSerializer = jsonSerializer(),
        serializer = defaultSerializer,
        socketFactory = defaultFactory,
        scheduleFactory,
        pendingFactory = usePendingRegistry,
        handshakeTimeoutMs = 15000,
    } = options

    let ws: WebSocket | null = null
    let state: ConnectionState = "disconnected"
    let manuallyClosed = false

    const schedule = scheduleFactory?.()
    const pending = pendingFactory()

    const connect = (): Promise<void> => {
        if (state === "connected" || state === "connecting") {
            return Promise.resolve()
        }

        state = "connecting"
        ws = null
        manuallyClosed = false

        return new Promise<void>((resolve, reject) => {
            let socket: WebSocket

            const onConnectError = (): void => {
                state = "disconnected"
                if (schedule) {
                    schedule.run(connect).then(resolve, reject)
                    return
                }
                const error = new Error("WebSocket connection error")
                reject(error)
            }

            const onRuntimeError = (): void => {
                state = "disconnected"
                if (schedule) {
                    schedule.run(connect)
                    return
                }
                const error = new Error("WebSocket runtime error")
                pending.rejectAll(error)
            }

            try {
                const URL = typeof url === 'string' ? url : url()
                ws = socket = socketFactory(URL)
            } catch (err) {
                return onConnectError()
            }

            socket.onopen = (): void => {
                sendHandshake(socket)
                    .then(() => {
                        state = "connected"

                        schedule?.reset()

                        socket.onerror = onRuntimeError
                        socket.onopen = null
                        socket.onmessage = (ev) => handleMessage(ev.data as string | BufferSource | Blob)

                        resolve()
                        resolveQueue(socket)
                    })
                    .catch((error: unknown) => {
                        state = "disconnected"
                        reject(error)
                        try { socket.close(4000, "handshake fail") } catch { }
                    })
            }
            socket.onmessage = (ev) => handleHandshakeMessage(ev.data as string | BufferSource | Blob)
            socket.onerror = onConnectError
            socket.onclose = (ev: { code: number; reason: string }): void => {
                state = "disconnected"

                const error = new Error(`Connection closed (code ${ev.code})`)
                pending.rejectAll(error)

                ws = null

                if (!manuallyClosed && schedule) {
                    schedule.run(connect).then(resolve, reject)
                    return
                }

                reject(error)
            }
        })
    }

    const close = (code?: number, reason?: string): void => {
        if (state !== "connected" || ws?.readyState !== WebSocket.OPEN) {
            throw new Error('Client not connected')
        }

        state = "disconnected"

        const socket = ws
        manuallyClosed = true
        ws = null

        try { socket?.close(code ?? 1000, reason) } catch { }

        const error = new Error(
            `Connection closed${reason ? ` (${reason})` : code ? ` (code ${code})` : ''}`,
        )
        pending.rejectAll(error)
        rejectQueue(error)
    }

    const HANDSHAKE_ID = 0
    const sendHandshake = (socket: WebSocket): Promise<{}> => {
        return new Promise<{}>((resolve, reject) => {
            if (socket.readyState !== WebSocket.OPEN) {
                reject(new Error("Socket is not open"))
                return
            }

            let message: string | ArrayBuffer | Uint8Array<ArrayBuffer>
            try {
                message = defaultSerializer.serialize({ protocol: serializer.protocol })
            } catch (err) {
                reject(err)
                return
            }

            const timer = setTimeout(() => {
                pending.remove(HANDSHAKE_ID)
                reject(new Error("Handshake timed out"))
            }, handshakeTimeoutMs)

            const settle = (fn: () => void) => {
                clearTimeout(timer)
                fn()
            }

            try {
                pending.enqueue(HANDSHAKE_ID, {
                    resolve: (v) => settle(() => resolve(v)),
                    reject: (e) => settle(() => reject(e)),
                })
                socket.send(message)
            } catch (err) {
                pending.remove(HANDSHAKE_ID)
                reject(err)
            }
        })
    }

    const toBytes = async (data: string | BufferSource | Blob): Promise<Encoded> => {
        if (typeof data === 'string')
            return data

        if (data instanceof ArrayBuffer)
            return data

        if (ArrayBuffer.isView(data))
            return new Uint8Array(data.buffer)

        return new Uint8Array(await data.arrayBuffer())
    }


    const handleHandshakeMessage = async (data: string | BufferSource | Blob): Promise<void> => {
        let value: unknown
        try {
            value = await defaultSerializer.deserialize(await toBytes(data))
        } catch (error) {
            pending.reject(HANDSHAKE_ID, error instanceof Error ? error : new Error("Unable to deserialize handshake response"))
            return
        }
        if (value && typeof value === "object" && Object.keys(value as object).length === 0) {
            pending.resolve(HANDSHAKE_ID, value)
            return
        }
        pending.reject(HANDSHAKE_ID, new Error("Handshake response has invalid format"))
    }

    const buildCorrelationId = () => {
        let correlationId = 0
        return () => {
            correlationId = (correlationId + 1) >>> 0
            if (correlationId === 0) correlationId = 1
            return correlationId
        }
    }
    const nextCorrelationId = buildCorrelationId()

    type QueueItem = {
        key: string
        value: unknown
        resolve: (value: any) => void
        reject: (reason?: unknown) => void
    }

    const queue: QueueItem[] = []

    const rejectQueue = (error: unknown) => {
        queue.splice(0).forEach(a => a.reject(error))
    }

    const resolveQueue = (socket: WebSocket) => {
        if (state !== "connected" || ws?.readyState !== WebSocket.OPEN) {
            return
        }

        queue.splice(0).forEach(({ key, value, resolve, reject }) => {
            const correlationId = nextCorrelationId()
            try {
                const message = serializer.serializeRequest({ key, id: correlationId, value })
                pending.enqueue(correlationId, { resolve, reject })
                socket.send(message)
            } catch (err) {
                pending.remove(correlationId)
                reject(err)
            }
        })
    }

    const send = <TRequest, TResponse>(key: string, value: TRequest): Promise<TResponse> => {
        if (state !== "connected" || ws?.readyState !== WebSocket.OPEN) {
            return new Promise<TResponse>((resolve, reject) => queue.push({ key, value, resolve, reject }))
        }

        const socket = ws
        const correlationId = nextCorrelationId()
        const message = serializer.serializeRequest({ id: correlationId, key, value })

        return new Promise<TResponse>((resolve, reject) => {
            try {
                pending.enqueue(correlationId, { resolve, reject })
                socket.send(message)
            } catch (error) {
                pending.remove(correlationId)
                reject(error)
            }
        })
    }

    const notify = <TRequest>(key: string, value: TRequest): void => {
        if (state !== "connected" || ws?.readyState !== WebSocket.OPEN) {
            throw new Error("Client is not connected")
        }

        const message = serializer.serializeRequest({ id: 0, key, value })
        ws.send(message)
    }

    const handlers = new Map<string, Set<Handler<unknown>>>()

    const receive = <TValue>(key: string, callback: Handler<TValue>): (() => void) => {
        const cb = callback as Handler<unknown>

        let store = handlers.get(key)
        if (!store) {
            handlers.set(key, (store = new Set()))
        }
        store.add(cb)

        return () => {
            const store = handlers.get(key)
            if (!store) { return }
            store.delete(cb)
            if (store.size === 0) {
                handlers.delete(key)
            }
        }
    }

    const handleMessage = async (data: string | BufferSource | Blob): Promise<void> => {
        const parsed = serializer.deserializeServer(await toBytes(data))

        switch (parsed.type) {
            case MessageType.response:
                pending.resolve(parsed.id, parsed.value)
                break
            case MessageType.event:
                const set = handlers.get(parsed.key)
                if (!set) return
                for (const handler of set) {
                    try { handler(parsed.value) }
                    catch (err) { }
                }
                break
            case MessageType.error:
                pending.reject(parsed.id, parsed.message)
                break
        }
    }

    return {
        state: () => state,
        connect,
        close,
        send,
        notify,
        receive
    }
}
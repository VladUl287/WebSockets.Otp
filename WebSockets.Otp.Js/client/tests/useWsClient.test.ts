import { describe, it, expect, beforeEach, afterEach, vi, vitest } from "vitest"
import { useWsClient, type WsClient, type WsClientOptions } from "../src/client"
import { MessageType, WsError, type Encoded, type MessageSerializer, type ServerMessage } from "@fefruess/websockets.otp.serializer.core"
import { buildSchedule } from "../src/schedule"

class MockWebSocket {
    static readonly CONNECTING = 0
    static readonly OPEN = 1
    static readonly CLOSING = 2
    static readonly CLOSED = 3

    readyState = MockWebSocket.CONNECTING
    onopen: (() => void) | null = null
    onmessage: ((ev: { data: string | BufferSource | Blob }) => void) | null = null
    onerror: (() => void) | null = null
    onclose: ((ev: { code: number; reason: string }) => void) | null = null

    sent: string[] = []

    constructor(public readonly url: string) { }

    send(data: string): void {
        if (this.readyState !== MockWebSocket.OPEN) throw new Error("Socket is not open")
        this.sent.push(data)
    }

    close(code?: number, reason?: string): void {
        this.readyState = MockWebSocket.CLOSED
        this.onclose?.({ code: code ?? 1000, reason: reason ?? "" })
    }

    triggerOpen(): void {
        this.readyState = MockWebSocket.OPEN
        this.onopen?.()
    }

    triggerMessage(data: string | BufferSource | Blob): void {
        this.onmessage?.({ data })
    }

    triggerError(): void {
        this.onerror?.()
    }

    triggerClose(code = 1000, reason = ""): void {
        this.readyState = MockWebSocket.CLOSED
        this.onclose?.({ code, reason })
    }
}

const decode = (d: Encoded): string => {
    if (typeof d === "string") return d
    if (d instanceof ArrayBuffer) return new TextDecoder().decode(new Uint8Array(d))
    return new TextDecoder().decode(d)
}

const createSerializer = (): MessageSerializer => ({
    protocol: "test",
    serialize: vitest.fn((d: unknown) => JSON.stringify(d)),
    deserialize: vitest.fn((s: Encoded) => JSON.parse(decode(s))),
    serializeRequest: vitest.fn((d: unknown) => JSON.stringify(d)),
    deserializeServer: vitest.fn((s: Encoded) => JSON.parse(decode(s)) as ServerMessage),
})

const flush = (): Promise<void> => new Promise<void>((r) => setTimeout(r, 0))

async function connected(options: Partial<WsClientOptions> = {}) {
    const socket = new MockWebSocket("ws://test")
    const client = useWsClient({
        url: "ws://test",
        socketFactory: () => socket as unknown as WebSocket,
        ...options,
    })
    const promise = client.connect()
    socket.triggerOpen()
    await flush()
    socket.triggerMessage(JSON.stringify({}))
    await flush()
    await promise
    return { client, socket }
}

let originalWebSocket: typeof WebSocket | undefined
beforeEach(() => {
    originalWebSocket = (globalThis as any).WebSocket
        ; (globalThis as any).WebSocket = MockWebSocket
})
afterEach(() => {
    ; (globalThis as any).WebSocket = originalWebSocket
    vitest.useRealTimers()
    vitest.restoreAllMocks()
})

describe("useWsClient", () => {
    describe("state", () => {
        it("starts disconnected", () => {
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => new MockWebSocket("ws://test") as unknown as WebSocket,
            })
            expect(client.state()).toBe("disconnected")
        })

        it("transitions to connecting then connected", async () => {
            const socket = new MockWebSocket("ws://test")
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => socket as unknown as WebSocket,
            })
            const p = client.connect()
            expect(client.state()).toBe("connecting")
            socket.triggerOpen()
            await flush()
            socket.triggerMessage(JSON.stringify({}))
            await flush()
            await p
            expect(client.state()).toBe("connected")
        })

        it("becomes disconnected after unexpected close", async () => {
            const { client, socket } = await connected()
            socket.triggerClose(1006, "abnormal")
            expect(client.state()).toBe("disconnected")
        })
    })

    describe("connect()", () => {
        it("resolves on successful handshake", async () => {
            const { client } = await connected()
            expect(client.state()).toBe("connected")
        })

        it("sends the handshake message using the handshake serializer", async () => {
            const socket = new MockWebSocket("ws://test")
            const hs = createSerializer()
            const client = useWsClient({
                url: "ws://test",
                handshakeSerializer: hs,
                socketFactory: () => socket as unknown as WebSocket,
            })
            const p = client.connect()
            socket.triggerOpen()
            expect(hs.serialize).toHaveBeenCalledWith({ protocol: hs.protocol })
            expect(socket.sent).toHaveLength(1)
            socket.triggerMessage(JSON.stringify({}))
            await flush()
            await p
        })

        it("uses serializer.protocol in the handshake payload", async () => {
            const socket = new MockWebSocket("ws://test")
            const handshakeSerializer = createSerializer()
            const serializer = { ...createSerializer(), protocol: "custom-proto" }
            const client = useWsClient({
                url: "ws://test",
                handshakeSerializer,
                serializer,
                socketFactory: () => socket as unknown as WebSocket,
            })
            const p = client.connect()
            socket.triggerOpen()
            expect(handshakeSerializer.serialize).toHaveBeenCalledWith({ protocol: "custom-proto" })
            socket.triggerMessage(JSON.stringify({}))
            await flush()
            await p
        })

        it("invokes URL function at connect time (not construction)", async () => {
            const urlFn = vitest.fn().mockReturnValue("ws://test")
            const socket = new MockWebSocket("ws://test")
            const client = useWsClient({
                url: urlFn as any,
                socketFactory: () => socket as unknown as WebSocket,
            })
            expect(urlFn).not.toHaveBeenCalled()
            const p = client.connect()
            expect(urlFn).toHaveBeenCalledTimes(1)
            socket.triggerOpen()
            await flush()
            socket.triggerMessage(JSON.stringify({}))
            await flush()
            await p
        })

        it("is a no-op when already connected", async () => {
            const { client } = await connected()
            await expect(client.connect()).resolves.toBeUndefined()
        })

        it("is a no-op while still connecting", async () => {
            const socket = new MockWebSocket("ws://test")
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => socket as unknown as WebSocket,
            })
            const p1 = client.connect()
            const p2 = client.connect()
            await expect(p2).resolves.toBeUndefined()
            socket.triggerOpen()
            await flush()
            socket.triggerMessage(JSON.stringify({}))
            await flush()
            await p1
        })

        it("rejects when the socket factory throws", async () => {
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => {
                    throw new Error("boom")
                },
            })
            await expect(client.connect()).rejects.toThrow("WebSocket connection error")
            expect(client.state()).toBe("disconnected")
        })

        it("rejects when the socket errors during the connection phase", async () => {
            const socket = new MockWebSocket("ws://test")
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => socket as unknown as WebSocket,
            })
            const p = client.connect()
            socket.triggerError()
            await expect(p).rejects.toThrow("WebSocket connection error")
            expect(client.state()).toBe("disconnected")
        })

        it("rejects on handshake timeout", async () => {
            vitest.useFakeTimers()
            const socket = new MockWebSocket("ws://test")
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => socket as unknown as WebSocket,
                handshakeTimeoutMs: 100,
            })
            const p = client.connect()
            socket.triggerOpen()
            vitest.advanceTimersByTime(100)
            await expect(p).rejects.toThrow("Handshake timed out")
            expect(client.state()).toBe("disconnected")
        })

        it("rejects on invalid handshake response format", async () => {
            const socket = new MockWebSocket("ws://test")
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => socket as unknown as WebSocket,
            })
            const p = client.connect()
            const assertion = expect(p).rejects.toThrow("Handshake response has invalid format")
            socket.triggerOpen()
            await flush()
            socket.triggerMessage(JSON.stringify({ not: "empty" }))
            await flush()
            await assertion
        })

        it("rejects when handshake deserialization fails", async () => {
            const socket = new MockWebSocket("ws://test")
            const serializer = createSerializer();
            (serializer.deserialize as any).mockImplementation(() => {
                throw new Error("bad json")
            })
            const client = useWsClient({
                url: "ws://test",
                handshakeSerializer: serializer,
                socketFactory: () => socket as unknown as WebSocket,
            })
            const p = client.connect()
            const assertion = expect(p).rejects.toThrow()
            socket.triggerOpen()
            await flush()
            socket.triggerMessage("not-json")
            await flush()
            await assertion
        })

        it("throws when no global WebSocket and no factory is provided", async () => {
            ; (globalThis as any).WebSocket = undefined
            const client = useWsClient({ url: "ws://test" })
            await expect(client.connect()).rejects.toThrow("WebSocket connection error")
        })

        it("delegates to schedule.run on connect error", async () => {
            const run = vitest.fn(() => Promise.resolve()).mockResolvedValue(undefined)
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => {
                    throw new Error("boom")
                },
                scheduleFactory: () => ({ run, reset: vitest.fn() }),
            })
            await expect(client.connect()).resolves.toBeUndefined()
            expect(run).toHaveBeenCalledWith(expect.any(Function))
        })

        it("reconnects via schedule after connection error", async () => {
            let attempts = 0
            const sockets: MockWebSocket[] = []
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => {
                    attempts++
                    const s = new MockWebSocket("ws://test")
                    sockets.push(s)
                    return s as unknown as WebSocket
                },
                scheduleFactory: buildSchedule({ minDelay: 5, jitter: 0, factor: 1 }),
            })
            const p = client.connect()
            sockets[0].triggerError()
            await new Promise((r) => setTimeout(r, 20))
            expect(attempts).toBe(2)
            sockets[1].triggerOpen()
            await flush()
            sockets[1].triggerMessage(JSON.stringify({}))
            await flush()
            await p
            expect(client.state()).toBe("connected")
        })

        it("resets the schedule after a successful connection", async () => {
            const reset = vitest.fn()
            const run = vitest.fn(() => Promise.resolve())
            const { client } = await connected({
                scheduleFactory: () => ({ run, reset }),
            })
            expect(reset).toHaveBeenCalled()
        })
    })

    describe("close()", () => {
        it("throws when not connected", () => {
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => new MockWebSocket("ws://test") as unknown as WebSocket,
            })
            expect(() => client.close()).toThrow("Client not connected")
        })

        it("throws when connecting (not yet open)", () => {
            const socket = new MockWebSocket("ws://test")
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => socket as unknown as WebSocket,
            })
            client.connect()
            expect(() => client.close()).toThrow("Client not connected")
        })

        it("closes the socket and marks disconnected", async () => {
            const { client, socket } = await connected()
            client.close(1000, "bye")
            expect(client.state()).toBe("disconnected")
            expect(socket.readyState).toBe(MockWebSocket.CLOSED)
        })

        it("does not trigger schedule.run on manual close", async () => {
            const run = vitest.fn(() => Promise.resolve())
            const { client } = await connected({
                scheduleFactory: () => ({ run, reset: vitest.fn() }),
            })
            client.close()
            expect(run).not.toHaveBeenCalled()
        })

        it("rejects pending requests with a close error", async () => {
            const { client, socket } = await connected()
            const sendPromise = client.send("foo", "bar")
            client.close(1000, "bye")
            await expect(sendPromise).rejects.toThrow(/Connection closed/)
        })
    })

    describe("send()", () => {
        it("queues while disconnected and dispatches after connect", async () => {
            const socket = new MockWebSocket("ws://test")
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => socket as unknown as WebSocket,
            })
            const sendPromise = client.send<string, string>("foo", "bar")
            const connectPromise = client.connect()
            socket.triggerOpen()
            await flush()
            socket.triggerMessage(JSON.stringify({}))
            await flush()
            await connectPromise

            expect(socket.sent).toHaveLength(2)
            const request = JSON.parse(socket.sent[1])
            expect(request.key).toBe("foo")
            expect(request.value).toBe("bar")
            expect(request.id).toBeGreaterThan(0)

            socket.triggerMessage(
                JSON.stringify({ type: MessageType.response, id: request.id, value: "ok" }),
            )
            await flush()
            await expect(sendPromise).resolves.toBe("ok")
        })

        it("sends immediately when connected", async () => {
            const { client, socket } = await connected()
            const sendPromise = client.send<string, string>("foo", "bar")
            expect(socket.sent).toHaveLength(2)
            const request = JSON.parse(socket.sent[1])
            socket.triggerMessage(
                JSON.stringify({ type: MessageType.response, id: request.id, value: "ok" }),
            )
            await flush()
            await expect(sendPromise).resolves.toBe("ok")
        })

        it("resolves with the correct value from response", async () => {
            const { client, socket } = await connected()
            const sendPromise = client.send<string, { hello: string }>("foo", "bar")
            const request = JSON.parse(socket.sent[1])
            socket.triggerMessage(
                JSON.stringify({
                    type: MessageType.response,
                    id: request.id,
                    value: { hello: "world" },
                }),
            )
            await flush()
            await expect(sendPromise).resolves.toEqual({ hello: "world" })
        })

        it("throws synchronously when serialization fails", async () => {
            const serializer = createSerializer()
                ; (serializer.serializeRequest as any).mockImplementationOnce(() => {
                    throw new Error("serialize-err")
                })
            const { client } = await connected({ serializer })
            expect(() => client.send("foo", "bar")).toThrow("serialize-err")
        })

        it("rejects when socket.send throws", async () => {
            const { client, socket } = await connected()
            vitest.spyOn(socket, "send").mockImplementationOnce(() => {
                throw new Error("send-err")
            })
            await expect(client.send("foo", "bar")).rejects.toThrow("send-err")
        })

        it("rejects with WsError when the server responds with an error", async () => {
            const { client, socket } = await connected()
            const sendPromise = client.send<string, string>("foo", "bar")
            const request = JSON.parse(socket.sent[1])

            const assertion = expect(sendPromise).rejects.toBeInstanceOf(WsError)

            socket.triggerMessage(
                JSON.stringify({
                    type: MessageType.error,
                    id: request.id,
                    code: 2,
                    message: "handler failed",
                }),
            )

            await flush()
            await assertion
        })

        it("assigns distinct correlation ids", async () => {
            const { client, socket } = await connected()
            client.send("a", 1)
            client.send("b", 2)
            const ids = socket.sent.slice(1).map((s) => JSON.parse(s).id)
            expect(new Set(ids).size).toBe(2)
        })
    })

    describe("notify()", () => {
        it("throws when not connected", () => {
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => new MockWebSocket("ws://test") as unknown as WebSocket,
            })
            expect(() => client.notify("k", "v")).toThrow("Client is not connected")
        })

        it("sends with id 0", async () => {
            const { client, socket } = await connected()
            client.notify("foo", { n: 1 })
            const sent = JSON.parse(socket.sent[1])
            expect(sent).toEqual({ id: 0, key: "foo", value: { n: 1 } })
        })
    })

    describe("receive()", () => {
        it("calls handlers on event messages", async () => {
            const { client, socket } = await connected()
            const handler = vitest.fn()
            client.receive("tick", handler)
            socket.triggerMessage(
                JSON.stringify({ type: MessageType.event, key: "tick", value: { n: 42 } }),
            )
            await flush()
            expect(handler).toHaveBeenCalledWith({ n: 42 })
        })

        it("invokes all handlers for the same key", async () => {
            const { client, socket } = await connected()
            const h1 = vitest.fn()
            const h2 = vitest.fn()
            client.receive("tick", h1)
            client.receive("tick", h2)
            socket.triggerMessage(JSON.stringify({ type: MessageType.event, key: "tick", value: 1 }))
            await flush()
            expect(h1).toHaveBeenCalledTimes(1)
            expect(h2).toHaveBeenCalledTimes(1)
        })

        it("ignores events for unknown keys", async () => {
            const { socket } = await connected()
            socket.triggerMessage(
                JSON.stringify({ type: MessageType.event, key: "unknown", value: 1 }),
            )
            await flush()
        })

        it("unsubscribe removes only the specific handler", async () => {
            const { client, socket } = await connected()
            const h1 = vitest.fn()
            const h2 = vitest.fn()
            const unsub1 = client.receive("tick", h1)
            client.receive("tick", h2)
            unsub1()
            socket.triggerMessage(JSON.stringify({ type: MessageType.event, key: "tick", value: 1 }))
            await flush()
            expect(h1).not.toHaveBeenCalled()
            expect(h2).toHaveBeenCalledTimes(1)
        })

        it("unsubscribing a missing key is a no-op", () => {
            const { client } = {} as { client: WsClient }
            const c = useWsClient({
                url: "ws://test",
                socketFactory: () => new MockWebSocket("ws://test") as unknown as WebSocket,
            })
            const unsub = c.receive("never", () => { })
            expect(() => unsub()).not.toThrow()
            expect(() => unsub()).not.toThrow()
        })

        it("isolates handler exceptions from other handlers", async () => {
            const { client, socket } = await connected()
            const throwing = vitest.fn(() => {
                throw new Error("boom")
            })
            const ok = vitest.fn()
            client.receive("tick", throwing)
            client.receive("tick", ok)
            socket.triggerMessage(JSON.stringify({ type: MessageType.event, key: "tick", value: 1 }))
            await flush()
            expect(throwing).toHaveBeenCalled()
            expect(ok).toHaveBeenCalled()
        })
    })

    describe("incoming message encoding", () => {
        it("handles string payloads", async () => {
            const { client, socket } = await connected()
            const handler = vitest.fn()
            client.receive("k", handler)
            socket.triggerMessage(JSON.stringify({ type: MessageType.event, key: "k", value: "s" }))
            await flush()
            expect(handler).toHaveBeenCalledWith("s")
        })

        it("handles ArrayBuffer payloads", async () => {
            const { client, socket } = await connected()
            const handler = vitest.fn()
            client.receive("k", handler)
            const bytes = new TextEncoder().encode(
                JSON.stringify({ type: MessageType.event, key: "k", value: "ab" }),
            )
            socket.triggerMessage(bytes.buffer)
            await flush()
            expect(handler).toHaveBeenCalledWith("ab")
        })

        it("handles Uint8Array payloads", async () => {
            const { client, socket } = await connected()
            const handler = vitest.fn()
            client.receive("k", handler)
            const bytes = new TextEncoder().encode(
                JSON.stringify({ type: MessageType.event, key: "k", value: "arr" }),
            )
            socket.triggerMessage(bytes)
            await flush()
            expect(handler).toHaveBeenCalledWith("arr")
        })

        it("handles Blob payloads", async () => {
            const { client, socket } = await connected()
            const handler = vitest.fn()
            client.receive("k", handler)
            const blob = new Blob([
                JSON.stringify({ type: MessageType.event, key: "k", value: "blob" }),
            ])
            socket.triggerMessage(blob)
            await flush()
            expect(handler).toHaveBeenCalledWith("blob")
        })
    })

    describe("runtime close", () => {
        it("rejects pending requests on unexpected close", async () => {
            const { client, socket } = await connected()
            const sendPromise = client.send("foo", "bar")
            socket.triggerClose(1006, "boom")
            await expect(sendPromise).rejects.toThrow(/Connection closed/)
            expect(client.state()).toBe("disconnected")
        })

        it("does not schedule reconnection on manual close", async () => {
            const run = vitest.fn(() => Promise.resolve())
            const { client } = await connected({
                scheduleFactory: () => ({ run, reset: vitest.fn() }),
            })
            client.close()
            expect(run).not.toHaveBeenCalled()
        })

        it("schedules reconnection on unexpected close", async () => {
            const run = vitest.fn(() => Promise.resolve()).mockResolvedValue(undefined)
            const { socket } = await connected({
                scheduleFactory: () => ({ run, reset: vitest.fn() }),
            })
            socket.triggerClose(1006, "abnormal")
            expect(run).toHaveBeenCalledWith(expect.any(Function))
        })

        it("uses onRuntimeError after handshake", async () => {
            const run = vitest.fn(() => Promise.resolve())
            const { socket } = await connected({
                scheduleFactory: () => ({ run, reset: vitest.fn() }),
            })
            socket.triggerError()
            expect(run).toHaveBeenCalled()
        })
    })

    describe("queued requests on connect failure", () => {
        it("rejects all queued sends when connect fails without a scheduler", async () => {
            const socket = new MockWebSocket("ws://test")
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => socket as unknown as WebSocket,
            })
            const p1 = client.send("a", 1)
            const p2 = client.send("b", 2)
            const connectP = client.connect()
            socket.triggerError()
            await expect(connectP).rejects.toThrow()
            await expect(p1).rejects.toThrow()
            await expect(p2).rejects.toThrow()
        })

        it("rejects queued sends when manual close happens during connecting", async () => {
            const socket = new MockWebSocket("ws://test")
            const client = useWsClient({
                url: "ws://test",
                socketFactory: () => socket as unknown as WebSocket,
            })
            const p = client.send("a", 1)
            const connectP = client.connect()
            socket.triggerError()
            await expect(connectP).rejects.toThrow()
            await expect(p).rejects.toThrow()
        })
    })
})
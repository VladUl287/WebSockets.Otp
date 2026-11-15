import type { Serializer } from "@fefruess/websockets.otp.serializer.core"

export const asBytes = async (data: BufferSource | Blob): Promise<Uint8Array> => {
    if (data instanceof ArrayBuffer)
        return new Uint8Array(data)

    if (ArrayBuffer.isView(data))
        return new Uint8Array(data.buffer)

    return new Uint8Array(await data.arrayBuffer())
}

const encoder = new TextEncoder()
const decoder = new TextDecoder()
export const jsonSerializer = (): Serializer => ({
    protocol: 'json',
    serialize: (data) => encoder.encode(JSON.stringify(data)),
    deserialize: async (data) => JSON.parse(typeof data === "string" ? data : decoder.decode(await asBytes(data)))
})

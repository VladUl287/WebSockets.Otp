export interface Serializer {
  readonly protocol: string
  readonly serialize: (data: unknown) => Uint8Array
  readonly deserialize: (data: string | BufferSource | Blob) => Promise<unknown>
}
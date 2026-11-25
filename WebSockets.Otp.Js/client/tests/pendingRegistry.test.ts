import { describe, it, expect, vitest } from "vitest"
import { usePendingRegistry } from "../src/pending"

type Entry = { resolve: any; reject: any }

const mkEntry = (): Entry => ({ resolve: vitest.fn(), reject: vitest.fn() })

describe("usePendingRegistry", () => {
    it("enqueue stores a request", () => {
        const reg = usePendingRegistry<number>()
        const entry = mkEntry()
        reg.enqueue(1, entry)
        expect(reg.resolve(1, "v")).toBe(true)
        expect(entry.resolve).toHaveBeenCalledWith("v")
    })

    it("enqueue ignores duplicate keys (keeps the first)", () => {
        const reg = usePendingRegistry<number>()
        const a = mkEntry()
        const b = mkEntry()
        reg.enqueue(1, a)
        reg.enqueue(1, b)
        reg.resolve(1, "v")
        expect(a.resolve).toHaveBeenCalledTimes(1)
        expect(b.resolve).not.toHaveBeenCalled()
    })

    it("resolve returns false for unknown keys", () => {
        const reg = usePendingRegistry<number>()
        expect(reg.resolve(99, "v")).toBe(false)
    })

    it("resolve removes the entry after resolving", () => {
        const reg = usePendingRegistry<number>()
        const entry = mkEntry()
        reg.enqueue(1, entry)
        reg.resolve(1, "v")
        expect(reg.resolve(1, "v")).toBe(false)
    })

    it("reject rejects and removes the entry", () => {
        const reg = usePendingRegistry<number>()
        const entry = mkEntry()
        reg.enqueue(1, entry)
        expect(reg.reject(1, "err")).toBe(true)
        expect(entry.reject).toHaveBeenCalledWith("err")
        expect(reg.reject(1, "again")).toBe(false)
    })

    it("reject returns false for unknown keys", () => {
        const reg = usePendingRegistry<number>()
        expect(reg.reject(99, "e")).toBe(false)
    })

    it("remove deletes the entry without resolving/rejecting", () => {
        const reg = usePendingRegistry<number>()
        const entry = mkEntry()
        reg.enqueue(1, entry)
        reg.remove(1)
        expect(entry.resolve).not.toHaveBeenCalled()
        expect(entry.reject).not.toHaveBeenCalled()
        expect(reg.resolve(1, "v")).toBe(false)
    })

    it("remove on missing key is a no-op", () => {
        const reg = usePendingRegistry<number>()
        expect(() => reg.remove(99)).not.toThrow()
    })

    it("rejectAll rejects every request and clears the registry", () => {
        const reg = usePendingRegistry<number>()
        const a = mkEntry()
        const b = mkEntry()
        reg.enqueue(1, a)
        reg.enqueue(2, b)
        const err = new Error("all")
        reg.rejectAll(err)
        expect(a.reject).toHaveBeenCalledWith(err)
        expect(b.reject).toHaveBeenCalledWith(err)
        expect(reg.resolve(1, "v")).toBe(false)
        expect(reg.resolve(2, "v")).toBe(false)
    })

    it("rejectAll on empty registry is a no-op", () => {
        const reg = usePendingRegistry<number>()
        expect(() => reg.rejectAll(new Error("x"))).not.toThrow()
    })
})
import { describe, it, expect, vitest, beforeEach, afterEach } from "vitest"
import { buildSchedule } from "../src/schedule"

describe("buildSchedule", () => {
    beforeEach(() => {
        vitest.useFakeTimers()
    })
    afterEach(() => {
        vitest.useRealTimers()
        vitest.restoreAllMocks()
    })

    it("returns an object with run and reset", () => {
        const schedule = buildSchedule({})()
        expect(typeof schedule.run).toBe("function")
        expect(typeof schedule.reset).toBe("function")
    })

    it("invokes the callback after the min delay", async () => {
        vitest.spyOn(Math, "random").mockReturnValue(0.5)
        const schedule = buildSchedule({ minDelay: 100, maxDelay: 1000, jitter: 0, factor: 2 })()
        const cb = vitest.fn(() => new Promise<string>(r => r)).mockResolvedValue("ok")
        const p = schedule.run(cb)
        expect(cb).not.toHaveBeenCalled()
        vitest.advanceTimersByTime(99)
        expect(cb).not.toHaveBeenCalled()
        vitest.advanceTimersByTime(1)
        await expect(p).resolves.toBe("ok")
        expect(cb).toHaveBeenCalledTimes(1)
    })

    it("applies exponential backoff between retries", async () => {
        vitest.spyOn(Math, "random").mockReturnValue(0.5)
        const schedule = buildSchedule({ minDelay: 100, maxDelay: 10000, factor: 2, jitter: 0 })()
        const cb = vitest.fn(() => Promise.resolve()).mockResolvedValue(undefined)

        // 1st: 100ms
        const p1 = schedule.run(cb)
        vitest.advanceTimersByTime(100)
        await p1

        // 2nd: 200ms
        const p2 = schedule.run(cb)
        vitest.advanceTimersByTime(199)
        expect(cb).toHaveBeenCalledTimes(1)
        vitest.advanceTimersByTime(1)
        await p2
        expect(cb).toHaveBeenCalledTimes(2)

        // 3rd: 400ms
        const p3 = schedule.run(cb)
        vitest.advanceTimersByTime(400)
        await p3
        expect(cb).toHaveBeenCalledTimes(3)
    })

    it("clamps the delay to maxDelay", async () => {
        vitest.spyOn(Math, "random").mockReturnValue(0.5)
        const schedule = buildSchedule({ minDelay: 100, maxDelay: 150, factor: 10, jitter: 0 })()
        const cb = vitest.fn(() => Promise.resolve()).mockResolvedValue(undefined)

        // 1st: 100ms
        const p1 = schedule.run(cb)
        vitest.advanceTimersByTime(100)
        await p1

        // 2nd would be 1000ms, clamped to 150ms
        const p2 = schedule.run(cb)
        vitest.advanceTimersByTime(150)
        await p2
        expect(cb).toHaveBeenCalledTimes(2)
    })

    it("applies positive jitter", async () => {
        vitest.spyOn(Math, "random").mockReturnValue(1) // +1 -> +jitter * base
        const schedule = buildSchedule({ minDelay: 100, maxDelay: 1000, factor: 1, jitter: 0.5 })()
        const cb = vitest.fn(() => Promise.resolve()).mockResolvedValue(undefined)
        const p = schedule.run(cb)
        // base = 100, jitterOffset = 100 * 0.5 * 1 = 50, delay = 150
        vitest.advanceTimersByTime(149)
        expect(cb).not.toHaveBeenCalled()
        vitest.advanceTimersByTime(1)
        await p
        expect(cb).toHaveBeenCalledTimes(1)
    })

    it("applies negative jitter", async () => {
        vitest.spyOn(Math, "random").mockReturnValue(0) // -1 -> -jitter * base
        const schedule = buildSchedule({ minDelay: 100, maxDelay: 1000, factor: 1, jitter: 0.5 })()
        const cb = vitest.fn(() => Promise.resolve()).mockResolvedValue(undefined)
        const p = schedule.run(cb)
        // base = 100, jitterOffset = 100 * 0.5 * (-1) = -50, delay = 50
        vitest.advanceTimersByTime(49)
        expect(cb).not.toHaveBeenCalled()
        vitest.advanceTimersByTime(1)
        await p
        expect(cb).toHaveBeenCalledTimes(1)
    })

    it("never produces a negative delay", async () => {
        vitest.spyOn(Math, "random").mockReturnValue(0)
        const schedule = buildSchedule({ minDelay: 1, maxDelay: 100, factor: 1, jitter: 10 })()
        const cb = vitest.fn(() => Promise.resolve()).mockResolvedValue(undefined)
        const p = schedule.run(cb)
        vitest.advanceTimersByTime(0)
        await p
        expect(cb).toHaveBeenCalled()
    })

    it("rejects when maxRetries is exhausted", async () => {
        vitest.spyOn(Math, "random").mockReturnValue(0.5)
        const schedule = buildSchedule({ minDelay: 10, jitter: 0, factor: 1, maxRetries: 2 })()

        const cb = vitest.fn(() => Promise.resolve()).mockResolvedValue(undefined)

        // run 1
        const p1 = schedule.run(cb)
        vitest.advanceTimersByTime(10)
        await p1

        // run 2
        const p2 = schedule.run(cb)
        vitest.advanceTimersByTime(10)
        await p2

        // run 3 must reject
        await expect(schedule.run(cb)).rejects.toThrow("Maximum reconnect attempts reached")
        expect(cb).toHaveBeenCalledTimes(2)
    })

    it("reset clears the retry counter", async () => {
        vitest.spyOn(Math, "random").mockReturnValue(0.5)
        const schedule = buildSchedule({ minDelay: 10, jitter: 0, factor: 1, maxRetries: 1 })()
        const cb = vitest.fn(() => Promise.resolve()).mockResolvedValue(undefined)

        const p1 = schedule.run(cb)
        vitest.advanceTimersByTime(10)
        await p1

        await expect(schedule.run(cb)).rejects.toThrow("Maximum reconnect attempts reached")

        schedule.reset()
        const p2 = schedule.run(cb)
        vitest.advanceTimersByTime(10)
        await p2
        expect(cb).toHaveBeenCalledTimes(2)
    })

    it("propagates callback rejection", async () => {
        vitest.spyOn(Math, "random").mockReturnValue(0.5)
        const schedule = buildSchedule({ minDelay: 10, jitter: 0, factor: 1 })()
        const cb = vitest.fn(() => Promise.resolve()).mockRejectedValue(new Error("cb-fail"))
        const p = schedule.run(cb)
        vitest.advanceTimersByTime(10)
        await expect(p).rejects.toThrow("cb-fail")
    })

    it("uses default options when none are provided", async () => {
        // Defaults: minDelay=1000, maxDelay=30000, factor=2, jitter=0.3
        vitest.spyOn(Math, "random").mockReturnValue(0.5) // jitter -> 0
        const schedule = buildSchedule()()
        const cb = vitest.fn(() => Promise.resolve()).mockResolvedValue(undefined)
        const p = schedule.run(cb)
        vitest.advanceTimersByTime(999)
        expect(cb).not.toHaveBeenCalled()
        vitest.advanceTimersByTime(1)
        await p
        expect(cb).toHaveBeenCalledTimes(1)
    })

    it("factor of 1 keeps the delay constant", async () => {
        vitest.spyOn(Math, "random").mockReturnValue(0.5)
        const schedule = buildSchedule({ minDelay: 100, factor: 1, jitter: 0, maxDelay: 1000 })()
        const cb = vitest.fn(() => Promise.resolve()).mockResolvedValue(undefined)

        for (let i = 0; i < 3; i++) {
            const p = schedule.run(cb)
            vitest.advanceTimersByTime(99)
            expect(cb).toHaveBeenCalledTimes(i)
            vitest.advanceTimersByTime(1)
            await p
            expect(cb).toHaveBeenCalledTimes(i + 1)
        }
    })
})
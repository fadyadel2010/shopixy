import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
    getPosRuntimeCacheStats,
    invalidatePosRuntimeCache,
    readPosRuntimeCache,
} from './posRuntimeCache'

describe('posRuntimeCache bounded caching', () => {
    beforeEach(() => {
        invalidatePosRuntimeCache({ reason: 'test', broadcast: false })
    })

    it('serves a fresh cached value without calling the loader again', async () => {
        let loadCount = 0

        const first = await readPosRuntimeCache('product-search:test', async () => {
            loadCount += 1
            return ['first-result']
        })

        const second = await readPosRuntimeCache('product-search:test', async () => {
            loadCount += 1
            return ['second-result']
        })

        expect(first).toEqual(['first-result'])
        expect(second).toEqual(['first-result'])
        expect(loadCount).toBe(1)
        expect(getPosRuntimeCacheStats().entries).toBe(1)
    })

    it('evicts least recently used entries when max entry count is exceeded', async () => {
        let aLoads = 0
        let bLoads = 0

        await readPosRuntimeCache('a', async () => {
            aLoads += 1
            return 'A1'
        }, { maxEntries: 2 })

        await readPosRuntimeCache('b', async () => {
            bLoads += 1
            return 'B1'
        }, { maxEntries: 2 })

        const cachedA = await readPosRuntimeCache('a', async () => {
            aLoads += 1
            return 'A2'
        }, { maxEntries: 2 })

        await readPosRuntimeCache('c', async () => 'C1', { maxEntries: 2 })

        const reloadedB = await readPosRuntimeCache('b', async () => {
            bLoads += 1
            return 'B2'
        }, { maxEntries: 2 })

        expect(cachedA).toBe('A1')
        expect(reloadedB).toBe('B2')
        expect(aLoads).toBe(1)
        expect(bLoads).toBe(2)
        expect(getPosRuntimeCacheStats().entries).toBeLessThanOrEqual(2)
    })

    it('does not cache stale concurrent loader results', async () => {
        let resolveFirst: ((value: string) => void) | undefined

        const first = readPosRuntimeCache(
            'same-key',
            () => new Promise<string>((resolve) => {
                resolveFirst = resolve
            }),
        )

        const second = readPosRuntimeCache('same-key', async () => 'fresh')

        expect(await second).toBe('fresh')
        resolveFirst?.('stale')
        expect(await first).toBe('stale')

        const cached = await readPosRuntimeCache('same-key', async () => 'unexpected')
        expect(cached).toBe('fresh')
    })

    it('evicts entries when maxBytes limit is exceeded', async () => {
        // Create large values that will exceed byte limit
        const largeValue1 = 'A'.repeat(2000) // ~2KB
        const largeValue2 = 'B'.repeat(2000) // ~2KB
        let firstReloaded = false
        let secondReloaded = false

        // Insert first large value
        const first = await readPosRuntimeCache('large-key-1', async () => {
            firstReloaded = true
            return largeValue1
        }, { maxBytes: 3000 }) // 3KB limit

        expect(first).toBe(largeValue1)
        expect(firstReloaded).toBe(true)
        firstReloaded = false

        // Insert second large value, should evict first due to byte limit
        const second = await readPosRuntimeCache('large-key-2', async () => {
            secondReloaded = true
            return largeValue2
        }, { maxBytes: 3000 })

        expect(second).toBe(largeValue2)
        expect(secondReloaded).toBe(true)
        secondReloaded = false

        // Request first key again - should be evicted and reloaded
        const firstAgain = await readPosRuntimeCache('large-key-1', async () => {
            firstReloaded = true
            return largeValue1
        }, { maxBytes: 3000 })

        expect(firstAgain).toBe(largeValue1)
        expect(firstReloaded).toBe(true) // Loader was called again
        expect(getPosRuntimeCacheStats().approxBytes).toBeLessThanOrEqual(3000)
    })

    it('expires entries after TTL and reloads them', async () => {
        let reloadCount = 0

        // Use fake timers for deterministic TTL testing
        vi.useFakeTimers()

        try {
            // Insert value with very short TTL
            const first = await readPosRuntimeCache('ttl-key', async () => {
                reloadCount += 1
                return 'initial-value'
            }, { ttlMs: 1 })

            expect(first).toBe('initial-value')
            expect(reloadCount).toBe(1)

            // Advance time to expire the TTL
            vi.advanceTimersByTime(10)

            // Request again - should be expired and reloaded
            const second = await readPosRuntimeCache('ttl-key', async () => {
                reloadCount += 1
                return 'reloaded-value'
            }, { ttlMs: 1 })

            expect(second).toBe('reloaded-value')
            expect(reloadCount).toBe(2) // Loader was called again due to expiration
        } finally {
            // Always restore real timers, even if assertions fail
            vi.useRealTimers()
        }
    })
})

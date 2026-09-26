import { afterEach, describe, expect, it, vi } from 'vitest'
import { runWithCrossTabRefreshLock } from './crossTabRefreshLock'

describe('cross-tab refresh locking', () => {
    const originalLocks = navigator.locks

    afterEach(() => {
        Object.defineProperty(navigator, 'locks', {
            configurable: true,
            value: originalLocks,
        })
    })

    it('uses an exclusive named Web Lock when the browser provides it', async () => {
        const request = vi.fn(async (_name, _options, operation) => operation())
        Object.defineProperty(navigator, 'locks', {
            configurable: true,
            value: { request },
        })

        await expect(runWithCrossTabRefreshLock('pos-session-refresh', async () => 'restored')).resolves.toBe('restored')
        expect(request).toHaveBeenCalledWith(
            'pos-session-refresh',
            { mode: 'exclusive' },
            expect.any(Function),
        )
    })

    it('executes directly when Web Locks are unavailable', async () => {
        Object.defineProperty(navigator, 'locks', {
            configurable: true,
            value: undefined,
        })
        const operation = vi.fn(async () => 'restored')

        await expect(runWithCrossTabRefreshLock('pos-session-refresh', operation)).resolves.toBe('restored')
        expect(operation).toHaveBeenCalledOnce()
    })
})

import { describe, expect, it } from 'vitest'
import { canReplayAfterAuthRefresh } from './api'

describe('API authentication refresh replay policy', () => {
    it('replays only idempotent reads after refresh', () => {
        expect(canReplayAfterAuthRefresh({ method: 'get' })).toBe(true)
        expect(canReplayAfterAuthRefresh({ method: 'HEAD' })).toBe(true)
        expect(canReplayAfterAuthRefresh({ method: 'post' })).toBe(false)
        expect(canReplayAfterAuthRefresh({ method: 'put' })).toBe(false)
        expect(canReplayAfterAuthRefresh({ method: 'patch' })).toBe(false)
        expect(canReplayAfterAuthRefresh({ method: 'delete' })).toBe(false)
    })
})

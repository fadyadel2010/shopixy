import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiClient } from './apiClient'
const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }))
vi.mock('../services/api', () => ({ default: api, API_REQUEST_TIMEOUT_MESSAGE: 'timeout-message',
    isRequestTimeout: (error: unknown) => error instanceof Error && error.message === 'timeout-message' }))
beforeEach(() => vi.clearAllMocks())
describe('marketplace client error boundary', () => {
    it('preserves valid server message, status and code', async () => {
        api.get.mockRejectedValue({ response: { status: 409, data: { error: 'Conflict', errorCode: 'CONFLICT' } } })
        await expect(apiClient.get('/fixture')).rejects.toEqual({ message: 'Conflict', status: 409, code: 'CONFLICT' })
    })
    it('does not admit object-valued server messages or codes as strings', async () => {
        api.get.mockRejectedValue({ response: { status: 503, data: { error: { private: 'payload' }, errorCode: ['bad-code'] } } })
        await expect(apiClient.get('/fixture')).rejects.toEqual({ message: 'An error occurred', status: 503, code: undefined })
    })
    it.each([null, undefined, 'opaque thrown value'])('normalizes non-object rejection %s without throwing inside error handling', async error => {
        api.get.mockRejectedValue(error)
        await expect(apiClient.get('/fixture')).rejects.toEqual({ message: 'Unknown error', status: 0 })
    })
    it('preserves timeout and network categories', async () => {
        api.get.mockRejectedValueOnce(new Error('timeout-message')).mockRejectedValueOnce({ request: {} })
        await expect(apiClient.get('/fixture')).rejects.toMatchObject({ code: 'REQUEST_TIMEOUT', status: 0 })
        await expect(apiClient.get('/fixture')).rejects.toEqual({ message: 'Network error. Please check your connection.', status: 0 })
    })
    it('forwards request bodies and successful responses without rewriting them', async () => {
        const body = { title: 'Fixture' }
        api.post.mockResolvedValue({ data: body })
        expect(await apiClient.post('/fixture', body)).toBe(body)
        expect(api.post).toHaveBeenCalledExactlyOnceWith('/fixture', body)
    })
})

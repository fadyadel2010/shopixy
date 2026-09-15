import { beforeEach, describe, expect, it, vi } from 'vitest'

const apiMock = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))

vi.mock('./api', () => ({
    default: apiMock,
    getSafeApiErrorMessage: (_error: unknown, fallback: string) => fallback,
}))

import {
    createCapitalizationOperationId,
    getCapitalizationErrorMessage,
    resolveCapitalizationAttempt,
    treasuryInitialCapitalizationApi,
} from './treasuryInitialCapitalizationApi'

describe('treasury initial capitalization authority helpers', () => {
    it('creates a server-compatible operation UUID and reuses it only for the same payload', () => {
        const first = resolveCapitalizationAttempt(null, 'payload-one')
        const retry = resolveCapitalizationAttempt(first, 'payload-one')
        const changed = resolveCapitalizationAttempt(first, 'payload-two')

        expect(createCapitalizationOperationId()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i)
        expect(retry).toBe(first)
        expect(changed.operationId).not.toBe(first.operationId)
    })

    it('maps stable backend authority codes to localized messages', () => {
        const t = (key: string) => key
        expect(getCapitalizationErrorMessage({ response: { data: { code: 'INVALID_TREASURY_DESTINATION' } } }, t))
            .toBe('treasuryCapitalization.errors.invalidDestination')
        expect(getCapitalizationErrorMessage({ response: { data: { code: 'INITIAL_CAPITALIZATION_ALREADY_EXISTS' } } }, t))
            .toBe('treasuryCapitalization.errors.alreadyExists')
        expect(getCapitalizationErrorMessage({ response: { data: { code: 'INSUFFICIENT_FUNDS' } } }, t))
            .toBe('treasuryCapitalization.errors.insufficientFunds')
        expect(getCapitalizationErrorMessage({ response: { status: 403, data: {} } }, t))
            .toBe('treasuryCapitalization.errors.permissionDenied')
    })
})

describe('treasury initial capitalization API contract', () => {
    beforeEach(() => vi.clearAllMocks())

    it('uses only the dedicated authority endpoints', async () => {
        apiMock.get.mockResolvedValueOnce({ data: [{ treasuryAccountId: 'cashbox-id' }] })
        apiMock.get.mockResolvedValueOnce({ data: [{ id: 'document-id' }] })
        apiMock.post.mockResolvedValue({ data: { id: 'document-id' } })

        await treasuryInitialCapitalizationApi.listDestinations()
        await treasuryInitialCapitalizationApi.list()
        await treasuryInitialCapitalizationApi.create({ operationId: 'operation-id' })
        await treasuryInitialCapitalizationApi.reverse('document-id', { operationId: 'reversal-id' })

        expect(apiMock.get).toHaveBeenNthCalledWith(1, '/treasury/initial-capitalizations/destinations', { signal: undefined, skipAutomaticRetry: true })
        expect(apiMock.get).toHaveBeenNthCalledWith(2, '/treasury/initial-capitalizations', { signal: undefined, skipAutomaticRetry: true })
        expect(apiMock.post).toHaveBeenNthCalledWith(1, '/treasury/initial-capitalizations', { operationId: 'operation-id' })
        expect(apiMock.post).toHaveBeenNthCalledWith(2, '/treasury/initial-capitalizations/document-id/reverse', { operationId: 'reversal-id' })
    })
})

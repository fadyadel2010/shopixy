import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
    BULK_MUTATION_LOCAL_ERROR_CODES,
    bulkMutationReducer,
    useBulkProductMutation,
} from './useBulkProductMutation'

const bulkActivateMock = vi.hoisted(() => vi.fn())
const bulkDeactivateMock = vi.hoisted(() => vi.fn())
const bulkPublishMock = vi.hoisted(() => vi.fn())
const bulkUnpublishMock = vi.hoisted(() => vi.fn())
const bulkDeleteMock = vi.hoisted(() => vi.fn())

vi.mock('../services/bulkProductApi', () => ({
    bulkActivate: bulkActivateMock,
    bulkDeactivate: bulkDeactivateMock,
    bulkPublish: bulkPublishMock,
    bulkUnpublish: bulkUnpublishMock,
    bulkDelete: bulkDeleteMock,
}))

type ProductRow = { id: string; rowVersion: string | null; name: string }

type HookProps = {
    products: ProductRow[]
    selectedProductIds: string[]
    refreshProducts?: () => Promise<boolean> | boolean
}

const productRow = (id: string, rowVersion: string | null = '1'): ProductRow => ({
    id,
    rowVersion,
    name: `P-${id}`,
})

const aggregate = (overrides: Record<string, unknown> = {}) => ({
    requested: 1,
    accepted: 1,
    processed: 1,
    unchanged: 0,
    failed: 0,
    correlationId: 'corr-test-1',
    results: [
        { productId: 'p1', status: 'processed', action: null, code: null, message: null, previousVersion: '7', newVersion: '8' },
    ],
    ...overrides,
})

function createDeferred<T>() {
    let resolve!: (value: T) => void
    let reject!: (reason?: unknown) => void
    const promise = new Promise<T>((res, rej) => {
        resolve = res
        reject = rej
    })
    return { promise, resolve, reject }
}

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

function renderMutation(initialProps: Partial<HookProps> = {}) {
    const fallbackRefresh = async () => true as const
    return renderHook(
        (next: Partial<HookProps>) => useBulkProductMutation({
            products: next.products ?? initialProps.products ?? [],
            selectedProductIds: next.selectedProductIds ?? initialProps.selectedProductIds ?? [],
            refreshProducts: next.refreshProducts ?? initialProps.refreshProducts ?? fallbackRefresh,
        }),
        { initialProps: {} },
    )
}

async function flushSettlement() {
    await act(async () => {
        await Promise.resolve()
        await Promise.resolve()
        await Promise.resolve()
    })
}

describe('useBulkProductMutation — synchronous snapshot and duplicate lock', () => {
    let errorSpy: ReturnType<typeof vi.spyOn>

    beforeEach(() => {
        bulkActivateMock.mockReset()
        bulkDeactivateMock.mockReset()
        bulkPublishMock.mockReset()
        bulkUnpublishMock.mockReset()
        bulkDeleteMock.mockReset()
        for (const fn of [bulkActivateMock, bulkDeactivateMock, bulkPublishMock, bulkUnpublishMock, bulkDeleteMock]) {
            fn.mockResolvedValue(aggregate())
        }
        errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    })

    afterEach(() => {
        errorSpy.mockRestore()
    })

    it('captures selected ids, row versions, the action and a generated frontend request id at start', () => {
        const refreshProducts = vi.fn(async () => true)
        const { result } = renderMutation({
            products: [productRow('p1', '7'), productRow('p2', '9')],
            selectedProductIds: ['p2'],
            refreshProducts,
        })

        let accepted: boolean | undefined
        act(() => {
            accepted = result.current.startBulkMutation('delete')
        })

        expect(accepted).toBe(true)
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
        expect(bulkDeleteMock.mock.calls[0]).toHaveLength(1)
        expect(bulkDeleteMock.mock.calls[0][0]).toEqual([{ productId: 'p2', expectedVersion: '9' }])
        expect(result.current.action).toBe('delete')
        expect(result.current.requestId).toMatch(UUID_PATTERN)
        expect(result.current.selectedItems).toEqual([{ productId: 'p2', expectedVersion: '9' }])
        expect(result.current.status).toBe('submitting')
        expect(result.current.isBulkMutating).toBe(true)

        return flushSettlement().then(() => {
            expect(result.current.status).toBe('success')
            expect(result.current.isBulkMutating).toBe(false)
        })
    })

    it('maps each of the five actions to exactly its Phase 6G.1 function', async () => {
        const cases = [
            ['activate', bulkActivateMock],
            ['deactivate', bulkDeactivateMock],
            ['publish', bulkPublishMock],
            ['unpublish', bulkUnpublishMock],
            ['delete', bulkDeleteMock],
        ] as const

        for (const [actionName, serviceFn] of cases) {
            for (const [, fn] of cases) {
                fn.mockClear()
            }

            const { result } = renderMutation({
                products: [productRow('p1', '5')],
                selectedProductIds: ['p1'],
            })

            act(() => {
                result.current.startBulkMutation(actionName)
            })

            expect(serviceFn).toHaveBeenCalledTimes(1)
            expect(serviceFn.mock.calls[0]).toHaveLength(1)
            for (const [, otherFn] of cases) {
                if (otherFn !== serviceFn) {
                    expect(otherFn).not.toHaveBeenCalled()
                }
            }
            await flushSettlement()
        }
    })

    it('rejects a second start in the same tick and issues exactly one API call', async () => {
        const deferred = createDeferred<ReturnType<typeof aggregate>>()
        bulkActivateMock.mockReturnValueOnce(deferred.promise)

        const { result } = renderMutation({
            products: [productRow('p1', '3'), productRow('p2', '4')],
            selectedProductIds: ['p1', 'p2'],
        })

        let first: boolean | undefined
        let second: boolean | undefined
        act(() => {
            first = result.current.startBulkMutation('activate')
            second = result.current.startBulkMutation('activate')
        })

        expect(first).toBe(true)
        expect(second).toBe(false)
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
        expect(result.current.status).toBe('submitting')

        await act(async () => {
            deferred.resolve(aggregate())
        })

        expect(result.current.status).toBe('success')
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
    })

    it('rejects unknown actions locally without any API request', () => {
        const { result } = renderMutation({
            products: [productRow('p1', '3')],
            selectedProductIds: ['p1'],
        })

        let accepted: boolean | undefined
        act(() => {
            accepted = result.current.startBulkMutation('frobnicate')
        })

        expect(accepted).toBe(false)
        expect(result.current.status).toBe('failed')
        expect(result.current.error.code).toBe(BULK_MUTATION_LOCAL_ERROR_CODES.INVALID_ACTION)
        expect(result.current.action).toBeNull()
        expect(result.current.requestId).toBeNull()
        expect(bulkActivateMock).not.toHaveBeenCalled()
        expect(bulkDeleteMock).not.toHaveBeenCalled()
        expect(result.current.isBulkMutating).toBe(false)
    })

    it('keeps the immutable snapshot tied to requestId/action while selection and rows change in flight', async () => {
        const deferred = createDeferred<ReturnType<typeof aggregate>>()
        bulkPublishMock.mockReturnValueOnce(deferred.promise)

        const { result, rerender } = renderMutation({
            products: [productRow('a', '1'), productRow('b', '2'), productRow('c', '3')],
            selectedProductIds: ['a', 'b', 'c'],
        })

        act(() => {
            result.current.startBulkMutation('publish')
        })

        const requestIdBefore = result.current.requestId
        const snapshotBefore = result.current.selectedItems

        rerender({
            products: [productRow('d', '8'), productRow('e', '9')],
            selectedProductIds: [],
        })

        await act(async () => {
            deferred.resolve(aggregate())
        })

        expect(bulkPublishMock.mock.calls[0][0]).toEqual([
            { productId: 'a', expectedVersion: '1' },
            { productId: 'b', expectedVersion: '2' },
            { productId: 'c', expectedVersion: '3' },
        ])
        expect(result.current.requestId).toBe(requestIdBefore)
        expect(result.current.action).toBe('publish')
        expect(result.current.selectedItems).toEqual(snapshotBefore)
        expect(result.current.status).toBe('success')
    })

    it('uses the latest canonical refetch after the Products view changes in flight', async () => {
        const deferred = createDeferred<ReturnType<typeof aggregate>>()
        const originalRefresh = vi.fn(async () => true)
        const latestRefresh = vi.fn(async () => true)
        bulkActivateMock.mockReturnValueOnce(deferred.promise)

        const rows = [productRow('p1', '7')]
        const { result, rerender } = renderMutation({
            products: rows,
            selectedProductIds: ['p1'],
            refreshProducts: originalRefresh,
        })

        act(() => {
            result.current.startBulkMutation('activate')
        })

        rerender({
            products: rows,
            selectedProductIds: ['p1'],
            refreshProducts: latestRefresh,
        })

        await act(async () => {
            deferred.resolve(aggregate())
        })

        await waitFor(() => expect(latestRefresh).toHaveBeenCalledTimes(1))
        expect(originalRefresh).not.toHaveBeenCalled()
        expect(result.current.status).toBe('success')
    })

    it('fails locally with zero API requests when a selected row is missing rowVersion and preserves selection ownership', () => {
        const refreshProducts = vi.fn(async () => true)
        const { result } = renderMutation({
            products: [productRow('p1', '7'), productRow('p2', '')],
            selectedProductIds: ['p1', 'p2'],
            refreshProducts,
        })

        let accepted: boolean | undefined
        act(() => {
            accepted = result.current.startBulkMutation('unpublish')
        })

        expect(accepted).toBe(false)
        expect(result.current.status).toBe('failed')
        expect(result.current.error.code).toBe(BULK_MUTATION_LOCAL_ERROR_CODES.MISSING_ROW_VERSION)
        expect(result.current.error.details).toEqual({ productId: 'p2' })
        expect(result.current.requestId).toBeNull()
        expect(bulkUnpublishMock).not.toHaveBeenCalled()
        // Missing version preserves selection: no canonical refetch is forced.
        expect(refreshProducts).not.toHaveBeenCalled()
    })

    it('reports stale selected ids locally with zero API requests and recovers via canonical refresh', async () => {
        const refreshProducts = vi.fn(async () => true)
        const { result } = renderMutation({
            products: [productRow('p1', '7')],
            selectedProductIds: ['ghost-id'],
            refreshProducts,
        })

        let accepted: boolean | undefined
        act(() => {
            accepted = result.current.startBulkMutation('activate')
        })

        expect(accepted).toBe(false)
        expect(result.current.status).toBe('failed')
        expect(result.current.error.code).toBe(BULK_MUTATION_LOCAL_ERROR_CODES.SELECTED_ROW_STALE)
        expect(result.current.error.details).toEqual({ productId: 'ghost-id' })
        expect(bulkActivateMock).not.toHaveBeenCalled()

        await waitFor(() => expect(refreshProducts).toHaveBeenCalledTimes(1))
        expect(result.current.refreshError).toBeNull()
    })

    it('refuses to start when nothing is selected', () => {
        const { result } = renderMutation({
            products: [productRow('p1', '7')],
            selectedProductIds: [],
        })

        act(() => {
            result.current.startBulkMutation('activate')
        })

        expect(result.current.status).toBe('failed')
        expect(result.current.error.code).toBe(BULK_MUTATION_LOCAL_ERROR_CODES.EMPTY_SELECTION)
        expect(bulkActivateMock).not.toHaveBeenCalled()
    })
})

describe('useBulkProductMutation — server aggregate classification', () => {
    beforeEach(() => {
        for (const fn of [bulkActivateMock, bulkDeactivateMock, bulkPublishMock, bulkUnpublishMock, bulkDeleteMock]) {
            fn.mockReset()
            fn.mockResolvedValue(aggregate())
        }
    })

    it('classifies all-processed aggregates as success, retains the full result, refetches and clears selection', async () => {
        const refreshProducts = vi.fn(async () => true)
        const payload = aggregate({ requested: 2, accepted: 2, processed: 1, unchanged: 1, failed: 0 })
        bulkActivateMock.mockResolvedValueOnce(payload)

        const { result } = renderMutation({
            products: [productRow('p1', '7')],
            selectedProductIds: ['p1'],
            refreshProducts,
        })

        act(() => {
            result.current.startBulkMutation('activate')
        })

        await waitFor(() => expect(result.current.status).toBe('success'))
        expect(result.current.result).toEqual(payload)
        expect(result.current.error).toBeNull()
        expect(refreshProducts).toHaveBeenCalledTimes(1)
    })

    it('classifies mixed outcomes as partial_success with the full result retained', async () => {
        const refreshProducts = vi.fn(async () => true)
        const payload = aggregate({ requested: 2, accepted: 2, processed: 1, unchanged: 0, failed: 1 })
        bulkDeleteMock.mockResolvedValueOnce(payload)

        const { result } = renderMutation({
            products: [productRow('p1', '7'), productRow('p2', '8')],
            selectedProductIds: ['p1', 'p2'],
            refreshProducts,
        })

        act(() => {
            result.current.startBulkMutation('delete')
        })

        await waitFor(() => expect(result.current.status).toBe('partial_success'))
        expect(result.current.result).toEqual(payload)
        expect(refreshProducts).toHaveBeenCalledTimes(1)
    })

    it('classifies all-item failures as failed while retaining the full result and still refreshing', async () => {
        const refreshProducts = vi.fn(async () => true)
        const payload = aggregate({ requested: 2, accepted: 2, processed: 0, unchanged: 0, failed: 2 })
        bulkPublishMock.mockResolvedValueOnce(payload)

        const { result } = renderMutation({
            products: [productRow('p1', '7'), productRow('p2', '8')],
            selectedProductIds: ['p1', 'p2'],
            refreshProducts,
        })

        act(() => {
            result.current.startBulkMutation('publish')
        })

        await waitFor(() => expect(result.current.status).toBe('failed'))
        expect(result.current.error).toBeNull()
        expect(result.current.result).toEqual(payload)
        expect(refreshProducts).toHaveBeenCalledTimes(1)
    })
})

describe('useBulkProductMutation — transport failures, no retry, refresh errors', () => {
    let errorSpy: ReturnType<typeof vi.spyOn>

    beforeEach(() => {
        for (const fn of [bulkActivateMock, bulkDeactivateMock, bulkPublishMock, bulkUnpublishMock, bulkDeleteMock]) {
            fn.mockReset()
            fn.mockResolvedValue(aggregate())
        }
        errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    })

    afterEach(() => {
        errorSpy.mockRestore()
    })

    it('retains the exact normalized network error, makes one call and never retries', async () => {
        const refreshProducts = vi.fn(async () => true)
        const normalizedError = Object.assign(new Error('Network unavailable.'), {
            status: null,
            code: 'NETWORK_ERROR',
            correlationId: null,
        })
        bulkActivateMock.mockRejectedValueOnce(normalizedError)

        const { result } = renderMutation({
            products: [productRow('p1', '7')],
            selectedProductIds: ['p1'],
            refreshProducts,
        })

        act(() => {
            result.current.startBulkMutation('activate')
        })

        await waitFor(() => expect(result.current.status).toBe('failed'))
        expect(result.current.error).toBe(normalizedError)
        expect(result.current.error.code).toBe('NETWORK_ERROR')
        expect(result.current.result).toBeNull()
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
        expect(refreshProducts).toHaveBeenCalledTimes(1)

        // Extra ticks prove there is no retry path.
        await flushSettlement()
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
    })

    it('retains a 409 conflict unchanged with a single call', async () => {
        const refreshProducts = vi.fn(async () => true)
        const conflictError = Object.assign(new Error('One or more products changed.'), {
            status: 409,
            code: 'ROWVERSION_CONFLICT',
            correlationId: 'corr-409',
        })
        bulkDeleteMock.mockRejectedValueOnce(conflictError)

        const { result } = renderMutation({
            products: [productRow('p1', '7')],
            selectedProductIds: ['p1'],
            refreshProducts,
        })

        act(() => {
            result.current.startBulkMutation('delete')
        })

        await waitFor(() => expect(result.current.status).toBe('failed'))
        expect(result.current.error).toBe(conflictError)
        expect(result.current.error.status).toBe(409)
        expect(result.current.error.code).toBe('ROWVERSION_CONFLICT')
        expect(result.current.error.correlationId).toBe('corr-409')
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
        expect(refreshProducts).toHaveBeenCalledTimes(1)
    })

    it('keeps mutation outcome intact and records a separate refreshError when the canonical refetch reports failure', async () => {
        const refreshProducts = vi.fn(async () => false)
        const payload = aggregate()
        bulkActivateMock.mockResolvedValueOnce(payload)

        const { result } = renderMutation({
            products: [productRow('p1', '7')],
            selectedProductIds: ['p1'],
            refreshProducts,
        })

        act(() => {
            result.current.startBulkMutation('activate')
        })

        await waitFor(() => expect(result.current.refreshError).toBeTruthy())
        expect(result.current.status).toBe('success')
        expect(result.current.result).toEqual(payload)
        expect(result.current.refreshError.code).toBe(BULK_MUTATION_LOCAL_ERROR_CODES.REFRESH_FAILED)
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
    })

    it('stores a thrown refresh error separately without reclassifying the mutation', async () => {
        const refreshFailure = new Error('refresh exploded')
        const refreshProducts = vi.fn(async () => {
            throw refreshFailure
        })
        const payload = aggregate()
        bulkDeactivateMock.mockResolvedValueOnce(payload)

        const { result } = renderMutation({
            products: [productRow('p1', '7')],
            selectedProductIds: ['p1'],
            refreshProducts,
        })

        act(() => {
            result.current.startBulkMutation('deactivate')
        })

        await waitFor(() => expect(result.current.refreshError).toBe(refreshFailure))
        expect(result.current.status).toBe('success')
        expect(result.current.result).toEqual(payload)
        expect(bulkDeactivateMock).toHaveBeenCalledTimes(1)
    })

    it('ignores a stale refresh failure after a newer mutation lifecycle starts', async () => {
        const staleRefresh = createDeferred<boolean>()
        const firstRefresh = vi.fn(() => staleRefresh.promise)
        const latestRefresh = vi.fn(async () => true)
        const secondMutation = createDeferred<ReturnType<typeof aggregate>>()
        bulkActivateMock.mockResolvedValueOnce(aggregate())
        bulkDeleteMock.mockReturnValueOnce(secondMutation.promise)

        const rows = [productRow('p1', '7'), productRow('p2', '8')]
        const { result, rerender } = renderMutation({
            products: rows,
            selectedProductIds: ['p1'],
            refreshProducts: firstRefresh,
        })

        act(() => {
            result.current.startBulkMutation('activate')
        })
        await waitFor(() => expect(firstRefresh).toHaveBeenCalledTimes(1))
        await waitFor(() => expect(result.current.status).toBe('success'))

        rerender({
            products: rows,
            selectedProductIds: ['p2'],
            refreshProducts: latestRefresh,
        })

        let accepted: boolean | undefined
        act(() => {
            accepted = result.current.startBulkMutation('delete')
        })
        expect(accepted).toBe(true)
        expect(result.current.status).toBe('submitting')

        await act(async () => {
            staleRefresh.reject(new Error('stale refresh failed'))
            await Promise.resolve()
        })

        expect(result.current.status).toBe('submitting')
        expect(result.current.action).toBe('delete')
        expect(result.current.refreshError).toBeNull()

        await act(async () => {
            secondMutation.resolve(aggregate())
        })
        await waitFor(() => expect(latestRefresh).toHaveBeenCalledTimes(1))
        expect(result.current.status).toBe('success')
        expect(result.current.refreshError).toBeNull()
    })
})

describe('useBulkProductMutation — unmount safety', () => {
    let errorSpy: ReturnType<typeof vi.spyOn>

    beforeEach(() => {
        for (const fn of [bulkActivateMock, bulkDeactivateMock, bulkPublishMock, bulkUnpublishMock, bulkDeleteMock]) {
            fn.mockReset()
            fn.mockResolvedValue(aggregate())
        }
        errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    })

    afterEach(() => {
        errorSpy.mockRestore()
    })

    it('does not settle, refresh or warn after unmount before settlement, and makes no second call', async () => {
        const refreshProducts = vi.fn(async () => true)
        const deferred = createDeferred<ReturnType<typeof aggregate>>()
        bulkDeleteMock.mockReturnValueOnce(deferred.promise)

        const { result, unmount } = renderMutation({
            products: [productRow('p1', '7')],
            selectedProductIds: ['p1'],
            refreshProducts,
        })

        act(() => {
            result.current.startBulkMutation('delete')
        })
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)

        unmount()

        await act(async () => {
            deferred.resolve(aggregate())
            await Promise.resolve()
            await Promise.resolve()
        })

        expect(errorSpy).not.toHaveBeenCalled()
        expect(refreshProducts).not.toHaveBeenCalled()
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
    })
})

describe('useBulkProductMutation — reset and lifecycle hygiene', () => {
    let errorSpy: ReturnType<typeof vi.spyOn>

    beforeEach(() => {
        for (const fn of [bulkActivateMock, bulkDeactivateMock, bulkPublishMock, bulkUnpublishMock, bulkDeleteMock]) {
            fn.mockReset()
            fn.mockResolvedValue(aggregate())
        }
        errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    })

    afterEach(() => {
        errorSpy.mockRestore()
    })

    it('refuses reset while in flight and returns a coherent idle state afterwards', async () => {
        const deferred = createDeferred<ReturnType<typeof aggregate>>()
        bulkActivateMock.mockReturnValueOnce(deferred.promise)

        const { result } = renderMutation({
            products: [productRow('p1', '7')],
            selectedProductIds: ['p1'],
        })

        let resetWhileInFlight: boolean | undefined
        act(() => {
            result.current.startBulkMutation('activate')
            resetWhileInFlight = result.current.resetBulkMutation()
        })
        expect(resetWhileInFlight).toBe(false)
        expect(result.current.status).toBe('submitting')

        await act(async () => {
            deferred.resolve(aggregate())
        })
        expect(result.current.status).toBe('success')

        let resetOutcome: boolean | undefined
        act(() => {
            resetOutcome = result.current.resetBulkMutation()
        })

        expect(resetOutcome).toBe(true)
        expect(result.current.status).toBe('idle')
        expect(result.current.action).toBeNull()
        expect(result.current.requestId).toBeNull()
        expect(result.current.selectedItems).toEqual([])
        expect(result.current.result).toBeNull()
        expect(result.current.error).toBeNull()
        expect(result.current.refreshError).toBeNull()
        expect(result.current.isBulkMutating).toBe(false)
    })

    it('never persists identifiers, never logs, and passes no explicit idempotency key to the service', async () => {
        const logSpy = vi.spyOn(console, 'log').mockImplementation(() => {})
        const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
        const infoSpy = vi.spyOn(console, 'info').mockImplementation(() => {})
        localStorage.clear()
        sessionStorage.clear()

        try {
            const { result } = renderMutation({
                products: [productRow('p1', '7'), productRow('p2', '9')],
                selectedProductIds: ['p1', 'p2'],
            })

            act(() => {
                result.current.startBulkMutation('activate')
            })
            await flushSettlement()

            expect(localStorage.length).toBe(0)
            expect(sessionStorage.length).toBe(0)
            expect(errorSpy).not.toHaveBeenCalled()
            expect(logSpy).not.toHaveBeenCalled()
            expect(warnSpy).not.toHaveBeenCalled()
            expect(infoSpy).not.toHaveBeenCalled()
            expect(bulkActivateMock).toHaveBeenCalledTimes(1)
            expect(bulkActivateMock.mock.calls[0]).toHaveLength(1)
        } finally {
            logSpy.mockRestore()
            warnSpy.mockRestore()
            infoSpy.mockRestore()
        }
    })
})

describe('useBulkProductMutation — reducer state machine guards', () => {
    it('walks idle -> preparing -> submitting -> terminal and refuses out-of-order transitions', () => {
        let state = bulkMutationReducer(undefined as never, { type: 'RESET' })
        expect(state.status).toBe('idle')

        state = bulkMutationReducer(state, {
            type: 'PREPARE',
            action: 'delete',
            requestId: 'req-1',
            selectedItems: [{ productId: 'p1', expectedVersion: '7' }],
        })
        expect(state.status).toBe('preparing')
        expect(state.action).toBe('delete')
        expect(state.requestId).toBe('req-1')

        // SETTLE outside submitting is an impossible combination -> ignored.
        const stillPreparing = bulkMutationReducer(state, { type: 'SETTLE', outcome: 'success', result: {} })
        expect(stillPreparing.status).toBe('preparing')

        state = bulkMutationReducer(state, { type: 'BEGIN_SUBMIT' })
        expect(state.status).toStrictEqual('submitting')

        state = bulkMutationReducer(state, { type: 'MUTATION_FAILED', error: new Error('boom') })
        expect(state.status).toBe('failed')
        expect(state.action).toBe('delete')
        expect(state.requestId).toBe('req-1')
        expect(state.selectedItems).toEqual([{ productId: 'p1', expectedVersion: '7' }])
        expect(state.error.message).toBe('boom')
        expect(state.result).toBeNull()
    })

    it('records refresh failures without disturbing the retained terminal state', () => {
        let state = bulkMutationReducer(undefined as never, { type: 'RESET' })
        state = bulkMutationReducer(state, { type: 'PREPARE', action: 'activate', requestId: 'r1', selectedItems: [] })
        state = bulkMutationReducer(state, { type: 'BEGIN_SUBMIT' })
        state = bulkMutationReducer(state, { type: 'SETTLE', outcome: 'partial_success', result: { failed: 1 } })

        const refreshed = bulkMutationReducer(state, { type: 'REFRESH_FAILED', error: new Error('nope') })
        expect(refreshed.status).toBe('partial_success')
        expect(refreshed.result).toEqual({ failed: 1 })
        expect(refreshed.refreshError.message).toBe('nope')
    })

    it('resets into a pristine idle snapshot from any retained terminal state', () => {
        let state = bulkMutationReducer(undefined as never, { type: 'RESET' })
        state = bulkMutationReducer(state, { type: 'PREPARE', action: 'publish', requestId: 'r2', selectedItems: [] })
        state = bulkMutationReducer(state, { type: 'BEGIN_SUBMIT' })
        state = bulkMutationReducer(state, { type: 'MUTATION_FAILED', error: new Error('x') })

        const reset = bulkMutationReducer(state, { type: 'RESET' })
        expect(reset).toEqual({
            status: 'idle',
            action: null,
            requestId: null,
            selectedItems: [],
            result: null,
            error: null,
            refreshError: null,
        })
    })
})

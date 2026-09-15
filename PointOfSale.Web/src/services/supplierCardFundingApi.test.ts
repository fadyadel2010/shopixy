import { beforeEach, describe, expect, it, vi } from 'vitest'

const apiMock = vi.hoisted(() => ({
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
}))

vi.mock('./api', () => ({
    default: apiMock,
    getSafeApiErrorMessage: (_error: unknown, fallback: string) => fallback,
}))

import {
    canAdministerSupplierCards,
    canAccessSupplierCards,
    formatSupplierCardLabel,
    getSupplierCardAuthorityFields,
    getSupplierCardErrorMessage,
    purchaseRequiresSupplierCardInstrument,
    resolveSupplierCardAttempt,
    supplierCardFundingApi,
} from './supplierCardFundingApi'

describe('supplier card funding authority helpers', () => {
    it.each([
        ['Cash', 100, null, 0, false],
        ['Credit', 0, 'Card', 0, false],
        ['Credit', 40, 'Cash', 0, false],
        ['Card', 100, null, 100, true],
        ['Mixed', 100, null, 60, true],
        ['Credit', 60, 'Card', 60, true],
        ['Credit', 60, 'Mixed', 25, true],
    ])('resolves %s purchase authority requirements', (paymentMethod, paidAmount, initialPaymentMethod, cardAmount, expected) => {
        expect(purchaseRequiresSupplierCardInstrument({
            paymentMethod,
            paidAmount,
            initialPaymentMethod,
            cardAmount,
        })).toBe(expected)
    })

    it('formats the backend identity without inventing an account default', () => {
        expect(formatSupplierCardLabel({ displayName: 'Company Visa', last4: '4521' }))
            .toBe('Company Visa ****4521')
        expect(formatSupplierCardLabel({ displayName: 'Purchasing Card', last4: null }))
            .toBe('Purchasing Card')
    })

    it('preserves the selected instrument identity for every required Card allocation', () => {
        const instrument = { id: 'instrument-4521', authorityVersion: 7 }
        expect(getSupplierCardAuthorityFields(true, instrument)).toEqual({
            fundingInstrumentId: 'instrument-4521',
            fundingInstrumentVersion: 7,
        })
        expect(getSupplierCardAuthorityFields(false, instrument)).toEqual({
            fundingInstrumentId: null,
            fundingInstrumentVersion: null,
        })
    })

    it('mirrors backend role policy', () => {
        expect(canAccessSupplierCards('Owner')).toBe(true)
        expect(canAccessSupplierCards('Admin')).toBe(true)
        expect(canAccessSupplierCards('Manager')).toBe(true)
        expect(canAccessSupplierCards('Cashier')).toBe(false)
        expect(canAccessSupplierCards('Warehouse')).toBe(false)
        expect(canAdministerSupplierCards('Owner')).toBe(true)
        expect(canAdministerSupplierCards('Admin')).toBe(true)
        expect(canAdministerSupplierCards('Manager')).toBe(false)
    })

    it('reuses one request identity for retries of the same logical action', () => {
        const first = resolveSupplierCardAttempt(null, 'same-payload', 'supplier-payment')
        const retry = resolveSupplierCardAttempt(first, 'same-payload', 'supplier-payment')
        const changed = resolveSupplierCardAttempt(first, 'changed-payload', 'supplier-payment')

        expect(retry).toBe(first)
        expect(changed.idempotencyKey).not.toBe(first.idempotencyKey)
    })

    it('maps supplier card authority failures to localized UX', () => {
        const t = (key: string) => key
        expect(getSupplierCardErrorMessage({
            response: { status: 409, data: { code: 'INSUFFICIENT_FUNDS', message: 'The selected account does not have sufficient available funds.' } },
        }, t)).toBe('supplierCards.errors.insufficientFunds')
        expect(getSupplierCardErrorMessage({
            response: { status: 400, data: { code: 'SUPPLIER_CARD_AUTHORITY_INVALID', message: 'Insufficient funded card balance.' } },
        }, t)).toBe('supplierCards.errors.insufficientBalance')
        expect(getSupplierCardErrorMessage({
            response: { status: 409, data: { message: 'Instrument authority changed. Reload before editing.' } },
        }, t)).toBe('supplierCards.errors.staleMutation')
        expect(getSupplierCardErrorMessage({ response: { status: 403, data: {} } }, t))
            .toBe('supplierCards.errors.permissionDenied')
        expect(getSupplierCardErrorMessage({ response: { status: 500, data: {} } }, t))
            .toBe('supplierCards.errors.serviceUnavailable')
    })
})

describe('supplier card funding API contract', () => {
    beforeEach(() => vi.clearAllMocks())

    it('loads only active instruments for payment flows by backend identity', async () => {
        apiMock.get.mockResolvedValue({ data: [{ id: 'instrument-1' }] })
        await expect(supplierCardFundingApi.list({ activeOnly: true })).resolves.toEqual([{ id: 'instrument-1' }])
        expect(apiMock.get).toHaveBeenCalledWith('/supplier-card-funding-instruments', {
            params: { activeOnly: true },
            signal: undefined,
        })
    })

    it('sends the same caller-owned idempotency key without replacing it', async () => {
        apiMock.post.mockResolvedValue({ data: { id: 'funding-1' } })
        const request = {
            fundingInstrumentVersion: 3,
            fundingSourceTreasuryAccountId: 'source-1',
            amount: 25,
            fundingDate: '2026-09-13T00:00:00.000Z',
            referenceNumber: 'REF-1',
            notes: null,
        }
        await supplierCardFundingApi.fund('instrument-1', request, 'supplier-card-funding-attempt-1')
        await supplierCardFundingApi.fund('instrument-1', request, 'supplier-card-funding-attempt-1')

        expect(apiMock.post).toHaveBeenCalledTimes(2)
        expect(apiMock.post.mock.calls[0][2].headers['Idempotency-Key']).toBe('supplier-card-funding-attempt-1')
        expect(apiMock.post.mock.calls[1][2].headers['Idempotency-Key']).toBe('supplier-card-funding-attempt-1')
    })

    it('uses the ledger-derived statement endpoint', async () => {
        apiMock.get.mockResolvedValue({ data: { openingBalance: 10, closingBalance: 20, transactions: [] } })
        await supplierCardFundingApi.statement('instrument-1', { from: '2026-09-01', to: '2026-09-13' })
        expect(apiMock.get).toHaveBeenCalledWith('/supplier-card-funding-instruments/instrument-1/statement', {
            params: { from: '2026-09-01', to: '2026-09-13' },
            signal: undefined,
        })
    })
})

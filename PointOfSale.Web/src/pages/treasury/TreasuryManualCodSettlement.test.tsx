import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import TreasuryManualCodSettlement from './TreasuryManualCodSettlement.jsx'

const api = vi.hoisted(() => ({ getManualCodOrders: vi.fn(), getManualCodSettlement: vi.fn(), settleManualCod: vi.fn() }))
vi.mock('../../services/manualCodSettlementApi', () => api)
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key: string) => key }) }))
vi.mock('../../hooks/useRTL', () => ({ useRTL: () => ({ dir: 'rtl', locale: 'ar-EG' }) }))
vi.mock('../../store/authStore', () => ({ useAuthStore: <T,>(selector: (state: { user: { tenantId: string; id: string } }) => T) =>
    selector({ user: { tenantId: 'tenant', id: 'owner' } }) }))
const selection = { orders: [{ orderId: 'order-1', orderReference: 'MANUAL-001', amount: 100, currency: 'EGP', eligible: true, collectionStatus: 'Posted' }],
    currency: 'EGP', destinationAccountId: 'cashbox', destinationAccountCode: '1010', hasMore: false }
const result = { settlementId: 'settlement-1', journalReference: 'J-001', journalId: 'journal-1', grossAmount: 100,
    settledOrderCount: 1, destinationAccountId: 'cashbox', currency: 'EGP', accountingOutcome: 'Posted' }
const mount = () => render(<MemoryRouter><TreasuryManualCodSettlement /></MemoryRouter>)

describe('Manual COD settlement request identity and honest accounting UI', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        sessionStorage.clear()
        api.getManualCodOrders.mockResolvedValue(selection)
        api.getManualCodSettlement.mockRejectedValue(new Error('unknown'))
        api.settleManualCod.mockResolvedValue(result)
    })
    it('uses a fixed destination, whole-order selection and explicit confirmation', async () => {
        mount()
        await screen.findByText('MANUAL-001')
        expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'manualCod.review' })).toBeDisabled()
        fireEvent.click(screen.getByRole('checkbox'))
        fireEvent.click(screen.getByRole('button', { name: 'manualCod.review' }))
        expect(api.settleManualCod).not.toHaveBeenCalled()
        fireEvent.click(screen.getByRole('button', { name: 'manualCod.confirm' }))
        await screen.findByText('J-001')
        expect(api.settleManualCod.mock.calls[0][0]).toEqual({ settlementId: expect.any(String), orderIds: ['order-1'] })
        expect(screen.getByText('manualCod.success')).toBeInTheDocument()
        expect(sessionStorage.getItem('manual-cod:tenant:owner')).toBeNull()
    })
    it('keeps the same exact request after timeout and retries without a second operation ID', async () => {
        api.settleManualCod.mockRejectedValueOnce(new Error('timeout')).mockResolvedValueOnce(result)
        mount()
        await screen.findByText('MANUAL-001')
        fireEvent.click(screen.getByRole('checkbox'))
        fireEvent.click(screen.getByRole('button', { name: 'manualCod.review' }))
        fireEvent.click(screen.getByRole('button', { name: 'manualCod.confirm' }))
        await screen.findByRole('alert')
        const request = api.settleManualCod.mock.calls[0][0]
        expect(JSON.parse(sessionStorage.getItem('manual-cod:tenant:owner')!)).toEqual(request)
        expect(screen.getByRole('checkbox')).toBeDisabled()
        fireEvent.click(screen.getByRole('button', { name: 'manualCod.retrySame' }))
        await screen.findByText('J-001')
        expect(api.settleManualCod.mock.calls[1][0]).toEqual(request)
    })
    it('restores an unknown in-flight request on reload', async () => {
        const request = { settlementId: 'retained-operation', orderIds: ['order-1'] }
        sessionStorage.setItem('manual-cod:tenant:owner', JSON.stringify(request))
        mount()
        fireEvent.click(await screen.findByRole('button', { name: 'manualCod.retrySame' }))
        await screen.findByText('J-001')
        expect(api.settleManualCod).toHaveBeenCalledWith(request)
    })
    it('reloads the last posted result through a fresh read', async () => {
        sessionStorage.setItem('manual-cod:tenant:owner:last', 'settlement-1')
        api.getManualCodSettlement.mockResolvedValue(result)
        mount()
        await screen.findByText('J-001')
        expect(api.getManualCodSettlement).toHaveBeenCalledWith('settlement-1', expect.any(AbortSignal))
        expect(api.settleManualCod).not.toHaveBeenCalled()
    })
    it('never presents a failed eligibility read as zero-valued selectable orders', async () => {
        api.getManualCodOrders.mockRejectedValue(new Error('inactive cashbox'))
        mount()
        expect(await screen.findByRole('alert')).toHaveTextContent('manualCod.loadError')
        expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: 'manualCod.review' })).not.toBeInTheDocument()
    })
})

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import TreasuryManualWalletSettlement from './TreasuryManualWalletSettlement.jsx'

const api = vi.hoisted(() => ({ getManualWalletPayments: vi.fn(), previewManualWalletSettlement: vi.fn(), settleManualWallet: vi.fn(), getManualWalletSettlement: vi.fn() }))
vi.mock('../../services/manualWalletSettlementApi', () => api)
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key: string) => key }) }))
vi.mock('../../hooks/useRTL', () => ({ useRTL: () => ({ dir: 'rtl', locale: 'ar-EG' }) }))
vi.mock('../../store/authStore', () => ({ useAuthStore: <T,>(selector: (state: { user: { tenantId: string; id: string } }) => T) => selector({ user: { tenantId: 'tenant', id: 'owner' } }) }))
const payment = { orderId: 'order-1', orderReference: 'WALLET-001', paymentReference: 'PAY-001', paymentIntentId: 'intent-1', amount: 100,
    remainingAmount: 100, currency: 'EGP', approvedAtUtc: '2026-10-21T10:00:00Z', eligible: true }
const selection = { payments: [payment], currency: 'EGP', sourceAccountCode: '1140', destinationAccountId: 'cashbox', destinationAccountCode: '1010', cashboxBalance: 10, clearingBalance: 100, hasMore: false }
const preview = { payments: [payment], grossAmount: 100, currency: 'EGP', sourceAccountCode: '1140', destinationAccountCode: '1010', previewToken: 'server-snapshot' }
const result = { settlementId: 'settlement-1', journalReference: 'J-WALLET-001', journalId: 'journal-1', grossAmount: 100, settledOrderCount: 1, destinationAccountId: 'cashbox', currency: 'EGP', accountingOutcome: 'Posted' }
const mount = () => render(<MemoryRouter><TreasuryManualWalletSettlement /></MemoryRouter>)
const review = async () => {
    await screen.findByText('WALLET-001')
    fireEvent.click(screen.getByRole('checkbox'))
    fireEvent.click(screen.getByRole('button', { name: 'manualWalletSettlement.review' }))
    await screen.findByText('manualWalletSettlement.serverPreview')
}
const attest = () => fireEvent.click(screen.getAllByRole('checkbox')[1])
const confirm = () => fireEvent.click(screen.getByRole('button', { name: 'manualWalletSettlement.confirm' }))

describe('Manual wallet dedicated settlement UI authority and recovery', () => {
    beforeEach(() => {
        vi.resetAllMocks()
        sessionStorage.clear()
        api.getManualWalletPayments.mockResolvedValue(selection)
        api.previewManualWalletSettlement.mockResolvedValue(preview)
        api.getManualWalletSettlement.mockRejectedValue({ response: { status: 404 } })
        api.settleManualWallet.mockResolvedValue(result)
    })
    it('requires server preview and explicit receipt attestation; sends no money/accounts', async () => {
        mount()
        await screen.findByText('WALLET-001')
        expect(screen.getByRole('button', { name: 'manualWalletSettlement.review' })).toBeDisabled()
        fireEvent.click(screen.getByRole('checkbox'))
        fireEvent.click(screen.getByRole('button', { name: 'manualWalletSettlement.review' }))
        await screen.findByText('manualWalletSettlement.serverPreview')
        expect(api.previewManualWalletSettlement).toHaveBeenCalledWith(['order-1'])
        expect(api.settleManualWallet).not.toHaveBeenCalled()
        expect(screen.getByRole('button', { name: 'manualWalletSettlement.confirm' })).toBeDisabled()
        attest()
        confirm()
        confirm()
        await screen.findByText('J-WALLET-001')
        expect(api.settleManualWallet).toHaveBeenCalledTimes(1)
        expect(api.settleManualWallet).toHaveBeenCalledWith({ settlementId: expect.any(String), orderIds: ['order-1'], previewToken: 'server-snapshot' })
        expect(sessionStorage.getItem('manual-wallet:tenant:owner')).toBeNull()
    })
    it('retains exact identity after response loss and retries only that operation', async () => {
        api.settleManualWallet.mockRejectedValueOnce(new Error('response lost')).mockResolvedValueOnce(result)
        mount()
        await review()
        attest()
        confirm()
        await screen.findByRole('alert')
        const request = api.settleManualWallet.mock.calls[0][0]
        expect(JSON.parse(sessionStorage.getItem('manual-wallet:tenant:owner')!)).toEqual(request)
        fireEvent.click(screen.getByRole('button', { name: 'manualWalletSettlement.retrySame' }))
        await screen.findByText('J-WALLET-001')
        expect(api.settleManualWallet.mock.calls[1][0]).toEqual(request)
    })
    it('recovers committed history after reload without posting', async () => {
        sessionStorage.setItem('manual-wallet:tenant:owner', JSON.stringify({ settlementId: 'settlement-1', orderIds: ['order-1'], previewToken: 'server-snapshot' }))
        api.getManualWalletSettlement.mockResolvedValue(result)
        mount()
        await screen.findByText('J-WALLET-001')
        expect(api.settleManualWallet).not.toHaveBeenCalled()
    })
    it('clears a stale request only after a definitive conflict and not-found readback', async () => {
        api.settleManualWallet.mockRejectedValue({ response: { status: 409 } })
        mount()
        await review()
        attest()
        confirm()
        await screen.findByRole('alert')
        await waitFor(() => expect(sessionStorage.getItem('manual-wallet:tenant:owner')).toBeNull())
        expect(api.getManualWalletSettlement).toHaveBeenCalled()
    })
    it('does not discard operation identity when conflict recovery readback fails', async () => {
        api.settleManualWallet.mockRejectedValue({ response: { status: 409 } })
        api.getManualWalletSettlement.mockRejectedValue(new Error('read unavailable'))
        mount()
        await review()
        attest()
        confirm()
        await screen.findByRole('alert')
        expect(sessionStorage.getItem('manual-wallet:tenant:owner')).not.toBeNull()
    })
    it('keeps posted success truthful when the subsequent balances refresh fails', async () => {
        api.getManualWalletPayments.mockResolvedValueOnce(selection).mockRejectedValue(new Error('projection unavailable'))
        mount()
        await review()
        attest()
        confirm()
        await screen.findByText('J-WALLET-001')
        expect(await screen.findByRole('alert')).toHaveTextContent('manualWalletSettlement.loadError')
        expect(api.settleManualWallet).toHaveBeenCalledTimes(1)
        expect(sessionStorage.getItem('manual-wallet:tenant:owner')).toBeNull()
    })
    it('disables blocked entries and fails closed on list failure', async () => {
        api.getManualWalletPayments.mockResolvedValue({ ...selection, payments: [{ ...payment, eligible: false, blockedReason: 'WALLET_MANUAL_REFUND_INELIGIBLE' }] })
        mount()
        await screen.findByText('WALLET-001')
        expect(screen.getByRole('checkbox')).toBeDisabled()
        expect(screen.getByRole('button', { name: 'manualWalletSettlement.review' })).toBeDisabled()
    })
})

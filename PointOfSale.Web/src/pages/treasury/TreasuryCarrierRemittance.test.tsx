import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { I18nextProvider } from 'react-i18next'
import { createInstance } from 'i18next'
import en from '../../i18n/locales/en.json'
import ar from '../../i18n/locales/ar.json'
import TreasuryCarrierRemittance from './TreasuryCarrierRemittance.jsx'

const state = vi.hoisted(() => ({ language: 'en', user: { tenantId: 'tenant', id: 'operator', effectivePermissions: ['storefront.payments.manage', 'finance.operate'] } }))
const api = vi.hoisted(() => ({ getCarrierRemittanceOrders: vi.fn(), getCarrierRemittance: vi.fn(), receiveCarrierCash: vi.fn() }))
vi.mock('../../services/carrierRemittanceApi', async importOriginal => ({ ...await importOriginal<Record<string, unknown>>(), ...api }))
vi.mock('../../store/authStore', () => ({ useAuthStore: <T,>(selector: (value: { user: typeof state.user }) => T) => selector({ user: state.user }) }))
vi.mock('../../hooks/useRTL', () => ({ useRTL: () => ({ dir: state.language === 'ar' ? 'rtl' : 'ltr', locale: state.language === 'ar' ? 'ar-EG' : 'en-US' }) }))
const resources = { en: { translation: en }, ar: { translation: ar } }
const selection = { orders: [
    { orderId: 'order-1', orderReference: 'CARRIER-001', amount: 100, currency: 'EGP', carrierCode: 'bosta', configurationId: 'config-1', accountId: 'account-1', environment: 'Sandbox' },
    { orderId: 'order-2', orderReference: 'CARRIER-002', amount: 50, currency: 'EGP', carrierCode: 'bosta', configurationId: 'config-2', accountId: 'account-2', environment: 'Sandbox' }
], currency: 'EGP', destinationAccountCode: '1010', hasMore: false }
const posted = (request: { operationId: string; receiptReference: string; receivedAt: string; orderIds: string[] }) => ({ operationId: request.operationId, originalOperatorId: 'operator', outcome: 'Posted', accountingOutcome: 'Posted',
    confirmationBasis: 'OperatorAttestedPhysicalReceipt', receiptChannel: 'PhysicalMerchantCash', receiptReference: request.receiptReference,
    receivedAtUtc: request.receivedAt, recordedAtUtc: '2026-10-09T10:00:00Z', postedAtUtc: '2026-10-09T10:00:00Z',
    journalId: 'journal-1', journalReference: 'JE-RECEIPT-001', destinationAccountId: 'cashbox', destinationAccountCode: '1010',
    grossAmount: 100, receivedAmount: 100, fees: 0, currency: 'EGP', carrierCode: 'bosta', configurationId: 'config-1', accountId: 'account-1', environment: 'Sandbox', orderIds: request.orderIds })
let i18n: ReturnType<typeof createInstance>
const text = (key: string) => i18n.t(`carrierRemittance.${key}`)
const mount = () => render(<I18nextProvider i18n={i18n}><MemoryRouter><TreasuryCarrierRemittance /></MemoryRouter></I18nextProvider>)
const fill = async () => {
    await screen.findByText('CARRIER-001')
    fireEvent.click(screen.getAllByRole('checkbox')[0])
    fireEvent.change(screen.getByLabelText(text('receivedAt')), { target: { value: '2026-01-01T10:00' } })
    fireEvent.change(screen.getByLabelText(text('receiptReference')), { target: { value: 'MERCHANT-R-001' } })
}
const submit = async () => {
    await fill()
    fireEvent.click(screen.getByLabelText(text('attestation')))
    fireEvent.click(screen.getByRole('button', { name: text('review') }))
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: text('confirm') })) })
}

describe.each(['en', 'ar'])('%s operator-attested physical carrier receipt', language => {
    beforeEach(async () => {
        vi.clearAllMocks(); sessionStorage.clear(); state.language = language
        state.user = { tenantId: 'tenant', id: 'operator', effectivePermissions: ['storefront.payments.manage', 'finance.operate'] }
        i18n = createInstance(); await i18n.init({ lng: language, fallbackLng: 'en', resources, interpolation: { escapeValue: false } })
        api.getCarrierRemittanceOrders.mockResolvedValue(selection)
        api.getCarrierRemittance.mockRejectedValue(new Error('unknown'))
        api.receiveCarrierCash.mockImplementation(async request => posted(request))
    })
    it('requires already-received cash attestation, receipt reference and actual time; submits no money or actor authority', async () => {
        mount(); await fill()
        expect(screen.getByText(text('boundary'))).toBeInTheDocument()
        expect(screen.getByRole('button', { name: text('review') })).toBeDisabled()
        expect(api.receiveCarrierCash).not.toHaveBeenCalled()
        fireEvent.click(screen.getByLabelText(text('attestation')))
        fireEvent.click(screen.getByRole('button', { name: text('review') }))
        expect(api.receiveCarrierCash).not.toHaveBeenCalled()
        await act(async () => { fireEvent.click(screen.getByRole('button', { name: text('confirm') })) })
        await screen.findByText('JE-RECEIPT-001')
        expect(api.receiveCarrierCash.mock.calls[0][0]).toEqual({ operationId: expect.any(String), orderIds: ['order-1'], receivedAt: expect.any(String),
            receiptReference: 'MERCHANT-R-001', actualReceiptAttested: true, receiptChannel: 'PhysicalMerchantCash', notes: null, evidenceReference: null })
        expect(screen.getByText(text('posted'))).toBeInTheDocument()
        expect(sessionStorage.getItem('carrier-receipt:tenant:operator')).toBeNull()
    })
    it('selects only whole orders from one immutable carrier/account/environment/currency group', async () => {
        mount(); await fill()
        expect(screen.getAllByRole('checkbox')[1]).toBeDisabled()
        expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument()
        expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
        expect(screen.getByText('Dr 1010 / Cr 1130')).toBeInTheDocument()
    })
    it('admits an exact multi-order decimal SQL receipt without binary floating-point drift', async () => {
        api.getCarrierRemittanceOrders.mockResolvedValue({ ...selection, orders: [
            { ...selection.orders[0], amount: 100.10 },
            { ...selection.orders[0], orderId: 'order-2', orderReference: 'CARRIER-002', amount: 200.20 }
        ] })
        api.receiveCarrierCash.mockImplementation(async request => ({ ...posted(request), grossAmount: 300.30, receivedAmount: 300.30 }))
        mount(); await fill()
        fireEvent.click(screen.getAllByRole('checkbox')[1])
        fireEvent.click(screen.getByLabelText(text('attestation')))
        fireEvent.click(screen.getByRole('button', { name: text('review') }))
        await act(async () => { fireEvent.click(screen.getByRole('button', { name: text('confirm') })) })
        expect(await screen.findByText('JE-RECEIPT-001')).toBeInTheDocument()
        expect(screen.queryByRole('alert')).not.toBeInTheDocument()
        expect(api.receiveCarrierCash.mock.calls[0][0].orderIds).toEqual(['order-1', 'order-2'])
    })
    it('preserves exact request identity and receipt facts after a lost response', async () => {
        api.receiveCarrierCash.mockRejectedValueOnce(new Error('timeout')).mockImplementationOnce(async request => posted(request))
        mount(); await submit(); await screen.findByRole('alert')
        const first = api.receiveCarrierCash.mock.calls[0][0]
        expect(JSON.parse(sessionStorage.getItem('carrier-receipt:tenant:operator')!)).toEqual(first)
        expect(screen.getByLabelText(text('receiptReference'))).toBeDisabled()
        await act(async () => { fireEvent.click(screen.getByRole('button', { name: text('retrySame') })) })
        await screen.findByText('JE-RECEIPT-001')
        expect(api.receiveCarrierCash.mock.calls[1][0]).toEqual(first)
    })
    it('retrieves a committed receipt on reload without another post', async () => {
        const request = { operationId: 'retained-operation', orderIds: ['order-1'], receivedAt: '2026-01-01T10:00:00Z', receiptReference: 'R-1' }
        sessionStorage.setItem('carrier-receipt:tenant:operator', JSON.stringify(request)); api.getCarrierRemittance.mockResolvedValue(posted(request))
        mount(); await screen.findByText('JE-RECEIPT-001')
        expect(api.receiveCarrierCash).not.toHaveBeenCalled()
        expect(api.getCarrierRemittance).toHaveBeenCalledWith('retained-operation', expect.any(AbortSignal))
    })
    it('presents reconciliation conflicts without success or a replacement operation', async () => {
        api.receiveCarrierCash.mockRejectedValue({ response: { status: 409, data: { code: 'REMITTANCE_RECEIPT_REFERENCE_REVIEW_REQUIRED' } } })
        mount(); await submit()
        expect(await screen.findByRole('alert')).toHaveTextContent(text('reviewRequired'))
        expect(screen.queryByText(text('posted'))).not.toBeInTheDocument()
        expect(sessionStorage.getItem('carrier-receipt:tenant:operator')).not.toBeNull()
    })
    it('rejects a bare HTTP success or provider-confirmed label as receipt accounting evidence', async () => {
        api.receiveCarrierCash.mockImplementation(async request => ({ ...posted(request), confirmationBasis: 'ProviderConfirmed' }))
        mount(); await submit(); expect(await screen.findByRole('alert')).toHaveTextContent(text('unknownOutcome'))
        expect(screen.queryByText('JE-RECEIPT-001')).not.toBeInTheDocument()
    })
    it('does not fetch or offer receipt execution without current permission', async () => {
        state.user.effectivePermissions = ['finance.operate']; mount()
        expect(screen.getByRole('alert')).toHaveTextContent(text('unauthorized'))
        expect(api.getCarrierRemittanceOrders).not.toHaveBeenCalled(); expect(api.receiveCarrierCash).not.toHaveBeenCalled()
    })
    it('fails closed when SQL eligibility is unavailable', async () => {
        api.getCarrierRemittanceOrders.mockRejectedValue(new Error('missing binding')); mount()
        expect(await screen.findByRole('alert')).toHaveTextContent(text('loadError'))
        expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
    })
    it.each(['amount', 'currency', 'account', 'configuration'])('rejects Posted %s that differs from the reviewed selection', async defect => {
        api.receiveCarrierCash.mockImplementation(async request => ({ ...posted(request),
            ...(defect === 'amount' ? { grossAmount: 999, receivedAmount: 999 } :
                defect === 'currency' ? { currency: 'USD' } : defect === 'account' ? { accountId: 'other-account' } : { configurationId: 'other-config' }) }))
        mount(); await submit(); await screen.findByRole('alert')
        expect(screen.queryByText(text('posted'))).not.toBeInTheDocument()
        expect(sessionStorage.getItem('carrier-receipt:tenant:operator')).not.toBeNull()
    })
    it('accepts the server canonical Unicode-normalized receipt reference without losing recovery', async () => {
        api.receiveCarrierCash.mockImplementation(async request => ({ ...posted(request), receiptReference: request.receiptReference.normalize('NFKC') }))
        mount(); await fill()
        fireEvent.change(screen.getByLabelText(text('receiptReference')), { target: { value: 'ＲＥＣＥＩＰＴ-001' } })
        fireEvent.click(screen.getByLabelText(text('attestation')))
        fireEvent.click(screen.getByRole('button', { name: text('review') }))
        await act(async () => { fireEvent.click(screen.getByRole('button', { name: text('confirm') })) })
        expect(await screen.findByText('JE-RECEIPT-001')).toBeInTheDocument()
        expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })
    it.each(['duplicate', 'currency'])('rejects malformed %s eligibility without offering a selection', async defect => {
        api.getCarrierRemittanceOrders.mockResolvedValue({ ...selection, orders: defect === 'duplicate' ? [selection.orders[0], selection.orders[0]] :
            [{ ...selection.orders[0], currency: 'USD' }] })
        mount(); expect(await screen.findByRole('alert')).toHaveTextContent(text('loadError'))
        expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
    })
})

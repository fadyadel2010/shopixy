import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import SupplierCardFundingInstruments from './SupplierCardFundingInstruments.jsx'

const cardApiMock = vi.hoisted(() => ({
    list: vi.fn(),
    listFundingSources: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    fund: vi.fn(),
    statement: vi.fn(),
}))
const authState = vi.hoisted(() => ({ role: 'Manager' }))
const translateMock = vi.hoisted(() => (key: string, fallback?: string) => fallback || key)
const toastMocks = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))

vi.mock('../services/supplierCardFundingApi', async (importOriginal) => ({
    ...await importOriginal<typeof import('../services/supplierCardFundingApi')>(),
    supplierCardFundingApi: cardApiMock,
}))

vi.mock('../store/authStore', () => ({
    useAuthStore: (selector: (state: unknown) => unknown) => selector({ user: { role: authState.role } }),
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: translateMock }),
}))

vi.mock('../components/Toast', () => ({
    useToast: () => toastMocks,
}))

vi.mock('../contexts/SettingsContext', () => ({
    useSettings: () => ({ formatCurrency: (value: number) => `EGP ${Number(value).toFixed(2)}` }),
}))

vi.mock('../hooks/useRTL', () => ({
    useRTL: () => ({ isRTL: false, dir: 'ltr' }),
}))

const instrument = {
    id: 'instrument-1',
    displayName: 'Company Visa',
    instrumentType: 'FundedCompanyCard',
    issuer: 'Visa',
    last4: '4521',
    isActive: true,
    authorityVersion: 2,
    ledgerAccountId: 'ledger-1',
    ledgerAccountCode: '1097',
    ledgerAccountName: 'Company Visa funding asset',
    fundingSourceTreasuryAccountId: 'source-1',
    fundingSourceName: 'Main Bank',
    fundingSourceType: 'Bank',
    availableBalance: 125,
}

const source = {
    id: 'source-1',
    name: 'Main Bank',
    type: 'Bank',
    ledgerAccountId: 'bank-ledger-1',
    accountCode: '1030',
}

describe('SupplierCardFundingInstruments', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        authState.role = 'Manager'
        cardApiMock.list.mockResolvedValue([instrument])
        cardApiMock.listFundingSources.mockResolvedValue([source])
        cardApiMock.fund.mockResolvedValue({ id: 'funding-1' })
        cardApiMock.statement.mockResolvedValue({
            instrumentId: instrument.id,
            instrument: instrument.displayName,
            openingBalance: 25,
            closingBalance: 125,
            transactions: [{
                journalId: 'journal-1',
                date: '2026-09-13T00:00:00Z',
                transactionType: 'SupplierCardFunding',
                sourceDocumentId: 'funding-1',
                sourceReference: 'REF-1',
                description: 'Fund supplier card',
                debit: 100,
                credit: 0,
                netMovement: 100,
                runningBalance: 125,
            }],
        })
    })

    it('shows ledger authority and limits Manager to view, fund, and statement actions', async () => {
        render(<SupplierCardFundingInstruments />)
        expect(await screen.findByText('Company Visa')).toBeInTheDocument()
        expect(screen.getByText('1097')).toBeInTheDocument()
        expect(screen.getByText('EGP 125.00')).toBeInTheDocument()
        expect(screen.getByText(/Restricted card funding for supplier purchases and payments only/)).toBeInTheDocument()
        expect(screen.queryByText('supplierCards.createCard')).not.toBeInTheDocument()
        expect(screen.queryByText('common.edit')).not.toBeInTheDocument()
        expect(screen.queryByText('supplierCards.deactivateCard')).not.toBeInTheDocument()
        expect(screen.getByText('supplierCards.fund')).toBeInTheDocument()
        expect(screen.getByText('supplierCards.statement')).toBeInTheDocument()
    })

    it('requires an explicit backend funding source and sends a caller-owned request key', async () => {
        const user = userEvent.setup()
        render(<SupplierCardFundingInstruments />)
        await user.click(await screen.findByText('supplierCards.fund'))

        expect(screen.getByLabelText('supplierCards.fundingInstrument')).toHaveValue('instrument-1')
        const sourceSelect = screen.getByLabelText('supplierCards.fundingSource')
        expect(sourceSelect).toHaveValue('')
        await user.selectOptions(sourceSelect, 'source-1')
        await user.type(screen.getByLabelText('supplierCards.fundingAmount'), '40')
        await user.click(screen.getByRole('button', { name: 'supplierCards.confirmFunding' }))

        await waitFor(() => expect(cardApiMock.fund).toHaveBeenCalledTimes(1))
        expect(cardApiMock.fund).toHaveBeenCalledWith(
            'instrument-1',
            expect.objectContaining({
                fundingInstrumentVersion: 2,
                fundingSourceTreasuryAccountId: 'source-1',
                amount: 40,
            }),
            expect.stringMatching(/^supplier-card-funding-/),
        )
    })

    it('keeps the funding form open and shows localized insufficient funds without retrying', async () => {
        const user = userEvent.setup()
        cardApiMock.fund.mockRejectedValue({
            response: {
                status: 409,
                data: {
                    code: 'INSUFFICIENT_FUNDS',
                    message: 'The selected account does not have sufficient available funds.',
                },
            },
        })
        render(<SupplierCardFundingInstruments />)
        await user.click(await screen.findByText('supplierCards.fund'))
        await user.selectOptions(screen.getByLabelText('supplierCards.fundingSource'), 'source-1')
        await user.type(screen.getByLabelText('supplierCards.fundingAmount'), '100')
        await user.click(screen.getByRole('button', { name: 'supplierCards.confirmFunding' }))

        expect(await screen.findByText('supplierCards.errors.insufficientFunds')).toBeInTheDocument()
        expect(screen.getByRole('dialog', { name: 'supplierCards.fundCard' })).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'supplierCards.confirmFunding' })).toBeEnabled()
        expect(cardApiMock.fund).toHaveBeenCalledTimes(1)
        expect(toastMocks.showError).toHaveBeenCalledWith('supplierCards.errors.insufficientFunds')
        expect(toastMocks.showSuccess).not.toHaveBeenCalled()
    })

    it('renders backend statement running balances without calculating a local balance', async () => {
        const user = userEvent.setup()
        render(<SupplierCardFundingInstruments />)
        await user.click(await screen.findByText('supplierCards.statement'))

        expect(await screen.findByText('journal-1')).toBeInTheDocument()
        expect(screen.getAllByText('EGP 125.00').length).toBeGreaterThan(0)
        expect(cardApiMock.statement).toHaveBeenCalledWith('instrument-1', expect.objectContaining({
            from: expect.any(String),
            to: expect.any(String),
        }))
    })
})

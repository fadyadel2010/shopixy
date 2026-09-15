import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import TreasuryInitialCapitalization from './TreasuryInitialCapitalization.jsx'

const capitalizationApiMock = vi.hoisted(() => ({
    listDestinations: vi.fn(),
    list: vi.fn(),
    create: vi.fn(),
    reverse: vi.fn(),
}))
const toastMocks = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
const translateMock = vi.hoisted(() => (key: string) => key)

vi.mock('../services/treasuryInitialCapitalizationApi', async (importOriginal) => ({
    ...await importOriginal<typeof import('../services/treasuryInitialCapitalizationApi')>(),
    treasuryInitialCapitalizationApi: capitalizationApiMock,
}))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: translateMock }) }))
vi.mock('../components/Toast', () => ({ useToast: () => toastMocks }))
vi.mock('../contexts/SettingsContext', () => ({
    useSettings: () => ({ formatCurrency: (value: number) => `EGP ${Number(value).toFixed(2)}` }),
}))
vi.mock('../hooks/useRTL', () => ({ useRTL: () => ({ isRTL: false, dir: 'ltr', locale: 'en-US' }) }))

const destination = {
    treasuryAccountId: '12d0cc2b-bd95-4ba0-a648-eaec82345c11',
    name: 'Main Cashbox',
    nameAr: 'الخزنة الرئيسية',
    type: 'Cashbox',
    ledgerAccountId: '9e1f5edf-1ce9-4590-b7e3-e506175ba73f',
    ledgerAccountCode: '1010',
    ledgerAccountName: 'Cash on Hand',
    currentBalance: 1000,
}

const postedDocument = {
    id: '3461bdd9-1720-414e-8dea-9dc600c96f41',
    documentNumber: 'TIC-000001',
    operationId: 'a30f8b93-1510-4c00-88d3-b69414c9b19c',
    cutoverDate: '2026-09-13T00:00:00Z',
    totalAmount: 1000,
    status: 'Posted',
    evidenceReference: 'COUNT-2026-09-13',
    createdAtUtc: '2026-09-13T12:00:00Z',
    postedJournalId: 'dc89c417-eabb-45cf-b8c2-e2fed565e220',
    traceId: 'capitalization-browser-trace',
    reversalJournalId: null,
    openingEquity: { accountCode: '3010', accountName: 'Opening Balance Equity' },
    allocations: [{
        id: 'allocation-id',
        treasuryAccountId: destination.treasuryAccountId,
        ledgerAccountId: destination.ledgerAccountId,
        ledgerAccountCode: '1010',
        treasuryAccountName: 'Main Cashbox',
        amount: 1000,
    }],
}

describe('TreasuryInitialCapitalization', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        capitalizationApiMock.listDestinations.mockResolvedValue([destination])
        capitalizationApiMock.list.mockResolvedValue([])
        capitalizationApiMock.create.mockResolvedValue(postedDocument)
        capitalizationApiMock.reverse.mockResolvedValue({ ...postedDocument, status: 'Reversed' })
    })

    it('posts selected backend identity with explicit evidence, declaration, and a UUID operation identity', async () => {
        const user = userEvent.setup()
        render(<TreasuryInitialCapitalization />)

        await user.selectOptions(await screen.findByLabelText('treasuryCapitalization.destination'), destination.treasuryAccountId)
        await user.type(screen.getByLabelText('treasuryCapitalization.amount'), '1000')
        await user.type(screen.getByLabelText('treasuryCapitalization.evidenceReference'), 'COUNT-2026-09-13')
        await user.click(screen.getByText('treasuryCapitalization.declaration'))
        await user.click(screen.getByText('treasuryCapitalization.confirmation'))
        await user.click(screen.getByRole('button', { name: 'treasuryCapitalization.post' }))

        await waitFor(() => expect(capitalizationApiMock.create).toHaveBeenCalledTimes(1))
        expect(capitalizationApiMock.create).toHaveBeenCalledWith(expect.objectContaining({
            operationId: expect.stringMatching(/^[0-9a-f-]{36}$/i),
            evidenceReference: 'COUNT-2026-09-13',
            declarationVersion: 'TREASURY_CUTOVER_FUNDS_V1',
            declarationAcknowledged: true,
            predecessorCapitalizationId: null,
            allocations: [{ treasuryAccountId: destination.treasuryAccountId, amount: 1000 }],
        }))
        expect(screen.getByLabelText('treasuryCapitalization.postingReceipt')).toHaveTextContent('dc89c417-eabb-45cf-b8c2-e2fed565e220')
        expect(screen.getByLabelText('treasuryCapitalization.postingReceipt')).toHaveTextContent('capitalization-browser-trace')
        expect(screen.getByRole('link', { name: 'treasuryCapitalization.viewJournal' })).toHaveAttribute(
            'href',
            `/treasury/general-ledger?accountId=${destination.ledgerAccountId}`,
        )
    })

    it('blocks a second create when a Posted authority exists and exposes reversal-only correction', async () => {
        capitalizationApiMock.list.mockResolvedValue([postedDocument])
        render(<TreasuryInitialCapitalization />)

        expect((await screen.findAllByText('TIC-000001')).length).toBeGreaterThan(0)
        expect(screen.queryByRole('button', { name: 'treasuryCapitalization.post' })).not.toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'treasuryCapitalization.reverse' })).toBeInTheDocument()
        expect(screen.getByText('treasuryCapitalization.activeBlocked')).toBeInTheDocument()
        expect(screen.getAllByText('EGP 1000.00').length).toBeGreaterThan(0)
    })

    it('requires a reason and explicit confirmation before sending a reversal', async () => {
        const user = userEvent.setup()
        capitalizationApiMock.list.mockResolvedValue([postedDocument])
        render(<TreasuryInitialCapitalization />)
        await user.click(await screen.findByRole('button', { name: 'treasuryCapitalization.reverse' }))

        const submit = screen.getByRole('button', { name: 'treasuryCapitalization.confirmReversal' })
        expect(screen.getByLabelText('treasuryCapitalization.reversalReason')).toHaveFocus()
        expect(submit).toBeDisabled()
        await user.type(screen.getByLabelText('treasuryCapitalization.reversalReason'), 'Correct verified count')
        await user.click(screen.getByText('treasuryCapitalization.reversalConfirmation'))
        await user.click(submit)

        await waitFor(() => expect(capitalizationApiMock.reverse).toHaveBeenCalledWith(
            postedDocument.id,
            expect.objectContaining({
                operationId: expect.stringMatching(/^[0-9a-f-]{36}$/i),
                reason: 'Correct verified count',
                confirmed: true,
            }),
        ))
    })
})

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import TreasuryGeneralLedger from './TreasuryGeneralLedger.jsx'

const api = vi.hoisted(() => ({
    getAccountSummaries: vi.fn(),
    getGeneralLedger: vi.fn(),
    getTrialBalance: vi.fn(),
    getDefaultDateRange: () => ({ from: new Date(2026, 9, 1), to: new Date(2026, 9, 8) }),
}))
const translate = vi.hoisted(() => (key: string, fallback?: string) => fallback || key)
const toast = vi.hoisted(() => ({ showSuccess: vi.fn() }))
vi.mock('../../services/financialReportsApi', () => api)
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: translate }) }))
vi.mock('../../contexts/SettingsContext', () => ({
    useSettings: () => ({ formatCurrency: (value: number) => `EGP ${Number(value).toFixed(2)}` }),
}))
vi.mock('../../components/Toast', () => ({ useToast: () => toast }))
vi.mock('../../components/ReportTruthBadge', () => ({ default: () => null }))
vi.mock('../../hooks/useRTL', () => ({ useRTL: () => ({ dir: 'ltr', locale: 'en-US' }) }))

const catalog = [
    { accountId: 'cash', accountCode: '1010', accountName: 'Main Cashbox' },
    { accountId: 'cod', accountCode: '1130', accountName: 'COD Clearing' },
    { accountId: 'inactive-history', accountCode: '9000', accountName: 'Archived history' },
]
const cashReport = {
    accountId: 'cash', accountCode: '1010', accountName: 'Main Cashbox',
    openingBalance: 10, closingBalance: 10, totalDebits: 0, totalCredits: 0, entries: [],
}
function Location() {
    return <output data-testid="location">{useLocation().search}</output>
}
function renderLedger(url = '/treasury/general-ledger') {
    return render(<MemoryRouter initialEntries={[url]}><TreasuryGeneralLedger /><Location /></MemoryRouter>)
}

describe('General Ledger account discovery and report truth', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        api.getAccountSummaries.mockResolvedValue(catalog)
        api.getTrialBalance.mockResolvedValue({ lines: [] })
        api.getGeneralLedger.mockResolvedValue(cashReport)
    })

    it('offers all ledger accounts even when the current month has no activity', async () => {
        renderLedger()
        expect(await screen.findByRole('option', { name: '1130 - COD Clearing' })).toBeInTheDocument()
        expect(screen.getByRole('option', { name: '9000 - Archived history' })).toBeInTheDocument()
        expect(api.getTrialBalance).not.toHaveBeenCalled()
        expect(api.getGeneralLedger).not.toHaveBeenCalled()
        expect(screen.getByText('Choose an account from the dropdown to view its transaction ledger')).toBeInTheDocument()
    })

    it('distinguishes an empty period from missing history and preserves account/date filters in the URL', async () => {
        renderLedger()
        await screen.findByRole('option', { name: '1010 - Main Cashbox' })
        fireEvent.change(screen.getByRole('combobox', { name: 'Account' }), { target: { value: 'cash' } })
        expect(await screen.findByText('No transactions in this period')).toBeInTheDocument()
        expect(screen.getAllByText('EGP 10.00').length).toBeGreaterThan(1)
        fireEvent.change(screen.getByLabelText('Start Date'), { target: { value: '2026-09-01' } })
        await waitFor(() => expect(screen.getByTestId('location').textContent).toContain('from=2026-09-01'))
        expect(screen.getByTestId('location').textContent).toContain('accountId=cash')
        expect(screen.getByTestId('location').textContent).toContain('to=2026-10-08')
        expect(api.getAccountSummaries).toHaveBeenCalledTimes(1)
    })

    it('restores deep-linked filters and loads the exact account report', async () => {
        renderLedger('/treasury/general-ledger?accountId=cash&from=2026-09-01&to=2026-09-30')
        await screen.findByText('No transactions in this period')
        expect(screen.getByLabelText('Start Date')).toHaveValue('2026-09-01')
        expect(screen.getByLabelText('End Date')).toHaveValue('2026-09-30')
        expect(api.getGeneralLedger).toHaveBeenCalledWith('cash', new Date(2026, 8, 1), new Date(2026, 8, 30), expect.objectContaining({ signal: expect.any(AbortSignal) }))
    })

    it('shows a retryable catalog failure instead of an apparently empty chart', async () => {
        api.getAccountSummaries.mockRejectedValueOnce(new Error('catalog unavailable'))
        renderLedger()
        expect(await screen.findByRole('alert')).toHaveTextContent('Failed to load report')
        expect(screen.getByRole('combobox')).toBeDisabled()
        fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
        expect(await screen.findByRole('option', { name: '1130 - COD Clearing' })).toBeInTheDocument()
        expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('does not present failed or foreign-account reads as zero balances', async () => {
        api.getGeneralLedger.mockRejectedValue(new Error('404 foreign account'))
        renderLedger('/treasury/general-ledger?accountId=foreign')
        expect(await screen.findByRole('alert')).toHaveTextContent('Failed to load report')
        expect(screen.queryByText('Current Balance')).not.toBeInTheDocument()
        expect(screen.queryByText('EGP 0.00')).not.toBeInTheDocument()
    })

    it('cancels an obsolete account read and ignores its late result', async () => {
        let resolveCash!: (value: unknown) => void
        api.getGeneralLedger.mockImplementation((id: string) => id === 'cash'
            ? new Promise(resolve => { resolveCash = resolve })
            : Promise.resolve({ ...cashReport, accountId: 'cod', accountCode: '1130', accountName: 'COD Clearing', openingBalance: 0, closingBalance: 0 }))
        renderLedger('/treasury/general-ledger?accountId=cash')
        await screen.findByRole('option', { name: '1130 - COD Clearing' })
        const signal = api.getGeneralLedger.mock.calls[0][3].signal
        fireEvent.change(screen.getByRole('combobox'), { target: { value: 'cod' } })
        await screen.findByRole('heading', { name: 'COD Clearing' })
        expect(signal.aborted).toBe(true)
        await act(async () => resolveCash(cashReport))
        expect(screen.getByRole('heading', { name: 'COD Clearing' })).toBeInTheDocument()
        expect(screen.queryByRole('heading', { name: 'Main Cashbox' })).not.toBeInTheDocument()
    })
})

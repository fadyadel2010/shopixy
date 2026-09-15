import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import TreasuryDashboard from './TreasuryDashboard.jsx'

const finApiMock = vi.hoisted(() => ({
    getDashboard: vi.fn(),
    getBalanceSheet: vi.fn(),
    getProfitAndLoss: vi.fn(),
    getClearingAging: vi.fn(),
    getTrialBalance: vi.fn(),
    getDefaultDateRange: vi.fn(),
    getFiscalYearRange: vi.fn(),
}))
const treasuryApiMock = vi.hoisted(() => ({ getTreasuryAccounts: vi.fn() }))
const navigateMock = vi.hoisted(() => vi.fn())
const toastMocks = vi.hoisted(() => ({ showError: vi.fn(), showSuccess: vi.fn() }))
const translateMock = vi.hoisted(() => (key: string, fallback?: string) => fallback || key)
const donutProps = vi.hoisted(() => ({ current: null as unknown }))

vi.mock('../../services/financialReportsApi', () => finApiMock)
vi.mock('../../services/treasuryApi', () => treasuryApiMock)
vi.mock('../../services/api', () => ({ isRequestCanceled: () => false }))
vi.mock('react-router-dom', () => ({ useNavigate: () => navigateMock }))
vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: translateMock, i18n: { language: 'en' } }),
}))
vi.mock('../../contexts/SettingsContext', () => ({
    useSettings: () => ({ formatCurrency: (v: number) => `EGP ${Number(v).toFixed(2)}` }),
}))
vi.mock('../../components/Toast', () => ({ useToast: () => toastMocks }))
vi.mock('../../components/ReportTruthBadge', () => ({ default: () => null }))
vi.mock('../../components/LineChart', () => ({ default: () => null }))
vi.mock('../../components/DonutChart', () => ({
    default: (props: { data: unknown }) => {
        donutProps.current = props
        return null
    },
}))
vi.mock('../../hooks/useRTL', () => ({ useRTL: () => ({ dir: 'ltr', locale: 'en' }) }))
vi.mock('../../utils/dateFormat', () => ({
    formatDate: () => 'Jan 1',
    formatTime: () => 'now',
}))

describe('TreasuryDashboard catalog classification', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        donutProps.current = null
        const range = { from: new Date('2026-09-01'), to: new Date('2026-09-15') }
        finApiMock.getDefaultDateRange.mockReturnValue(range)
        finApiMock.getDashboard.mockResolvedValue({ netProfit: 5, revenue: 100, cogs: 10, expenses: 5, pendingClearingAmount: 777 })
        finApiMock.getBalanceSheet.mockResolvedValue({ assetLines: [] })
        finApiMock.getProfitAndLoss.mockResolvedValue({})
        finApiMock.getClearingAging.mockResolvedValue({ lines: [] })
        finApiMock.getTrialBalance.mockResolvedValue({ isBalanced: true })
        treasuryApiMock.getTreasuryAccounts.mockResolvedValue([
            { accountCode: '9001', name: 'Harbor Vault', nameAr: '', currentBalance: 400, accountType: 'Cashbox' },
            { accountCode: '9002', name: 'Harbor Bank', nameAr: '', currentBalance: 490, accountType: 'Bank' },
            { accountCode: '9003', name: 'Harbor Clearing', nameAr: '', currentBalance: 55, accountType: 'Clearing' },
            { accountCode: '9020', name: 'Unmapped Asset', nameAr: '', currentBalance: 9999, accountType: 'Asset' },
        ])
    })

    it('derives available cash from accountType and keeps clearing out of the cash chart', async () => {
        render(<TreasuryDashboard />)
        await waitFor(() => expect(treasuryApiMock.getTreasuryAccounts).toHaveBeenCalled())
        await waitFor(() => expect(donutProps.current).not.toBeNull())
        const data = (donutProps.current as { data: Array<{ label: string; value: number }> }).data
        const total = data.reduce((sum, d) => sum + d.value, 0)
        // Available Cashbox + Bank only; unmapped 9999 and clearing 55 excluded.
        expect(total).toBe(890)
        expect(data.some(d => d.value === 55)).toBe(false)
        expect(await screen.findByText('EGP 890.00')).toBeInTheDocument()
        expect(screen.getAllByText('EGP 55.00').length).toBeGreaterThanOrEqual(2)
        expect(screen.queryByText('EGP 777.00')).not.toBeInTheDocument()
        expect(screen.getByText(/never spendable cash/)).toBeInTheDocument()
        expect(screen.getByRole('heading', { name: /Treasury & Finance/ })).toBeInTheDocument()
    })
})

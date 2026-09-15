import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import TreasuryAccountViewer from './TreasuryAccountViewer.jsx'

const treasuryApiMock = vi.hoisted(() => ({
    getTreasuryAccounts: vi.fn(),
    getTreasuryBalance: vi.fn(),
    getTreasuryMovements: vi.fn(),
}))
const routeState = vi.hoisted(() => ({ accountCode: '9001' }))
const navigateMock = vi.hoisted(() => vi.fn())
const toastMocks = vi.hoisted(() => ({ showError: vi.fn(), showSuccess: vi.fn() }))
const translateMock = vi.hoisted(() => (key: string, fallback?: string) => fallback || key)

vi.mock('../../services/treasuryApi', () => treasuryApiMock)
vi.mock('../../services/api', () => ({ isRequestCanceled: () => false }))
vi.mock('react-router-dom', () => ({
    useNavigate: () => navigateMock,
    useParams: () => ({ accountCode: routeState.accountCode }),
}))
vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: translateMock, i18n: { language: 'en' } }),
}))
vi.mock('../../contexts/SettingsContext', () => ({
    useSettings: () => ({ formatCurrency: (v: number) => `EGP ${Number(v).toFixed(2)}` }),
}))
vi.mock('../../components/Toast', () => ({ useToast: () => toastMocks }))
vi.mock('../../components/ReportTruthBadge', () => ({ default: () => null }))
vi.mock('../../hooks/useRTL', () => ({ useRTL: () => ({ dir: 'ltr', locale: 'en' }) }))
vi.mock('../../utils/dateFormat', () => ({
    formatDate: (d: unknown) => String(d),
    formatDateInputValue: () => '2026-09-16',
}))

const catalog = [
    { accountCode: '9001', name: 'Harbor Vault', nameAr: '', currentBalance: 400, accountType: 'Cashbox', isClearing: false, preventNegative: true },
]

describe('TreasuryAccountViewer catalog identity', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        routeState.accountCode = '9001'
        treasuryApiMock.getTreasuryAccounts.mockResolvedValue(catalog)
        treasuryApiMock.getTreasuryBalance.mockResolvedValue({ accountCode: '9001', balance: 400 })
        treasuryApiMock.getTreasuryMovements.mockResolvedValue([])
    })

    it('uses catalog identity and shows role semantics', async () => {
        render(<TreasuryAccountViewer />)
        await waitFor(() => expect(treasuryApiMock.getTreasuryBalance).toHaveBeenCalledWith('9001', expect.anything()))
        expect(await screen.findByText('Harbor Vault')).toBeInTheDocument()
        expect(screen.getByText(/Treasury role/)).toBeInTheDocument()
        expect(screen.getByText(/Cashbox/)).toBeInTheDocument()
    })

    it('fails closed for an absent mapping without balance/movement calls', async () => {
        routeState.accountCode = '1020'
        render(<TreasuryAccountViewer />)
        await waitFor(() => expect(treasuryApiMock.getTreasuryAccounts).toHaveBeenCalled())
        await screen.findByText(/not part of the Treasury catalog/)
        expect(treasuryApiMock.getTreasuryBalance).not.toHaveBeenCalled()
        expect(treasuryApiMock.getTreasuryMovements).not.toHaveBeenCalled()
        expect(screen.queryByText(/Account 1020/)).not.toBeInTheDocument()
    })

    it('fails closed when a returned catalog row has an unknown account type', async () => {
        routeState.accountCode = '9020'
        treasuryApiMock.getTreasuryAccounts.mockResolvedValue([
            { accountCode: '9020', name: 'Mystery Asset', currentBalance: 999, accountType: 'Mystery' },
        ])

        render(<TreasuryAccountViewer />)

        expect(await screen.findByText(/not part of the Treasury catalog/)).toBeInTheDocument()
        expect(treasuryApiMock.getTreasuryBalance).not.toHaveBeenCalled()
        expect(treasuryApiMock.getTreasuryMovements).not.toHaveBeenCalled()
        expect(screen.queryByText('Mystery Asset')).not.toBeInTheDocument()
    })
})

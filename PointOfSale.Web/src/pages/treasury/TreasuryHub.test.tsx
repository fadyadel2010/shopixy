import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import TreasuryHub from './TreasuryHub.jsx'

const treasuryApiMock = vi.hoisted(() => ({
    getTreasuryAccounts: vi.fn(),
    getTreasuryDashboard: vi.fn(),
}))
const navigateMock = vi.hoisted(() => vi.fn())
const toastMocks = vi.hoisted(() => ({ showError: vi.fn(), showSuccess: vi.fn() }))
const langState = vi.hoisted(() => ({ language: 'en' }))
const translateMock = vi.hoisted(() => (key: string, fallback?: string) => fallback || key)

vi.mock('../../services/treasuryApi', () => treasuryApiMock)
vi.mock('../../services/api', () => ({ isRequestCanceled: () => false }))
vi.mock('react-router-dom', () => ({ useNavigate: () => navigateMock }))
vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: translateMock, i18n: langState }),
}))
vi.mock('../../contexts/SettingsContext', () => ({
    useSettings: () => ({ formatCurrency: (v: number) => `EGP ${Number(v).toFixed(2)}` }),
}))
vi.mock('../../components/Toast', () => ({ useToast: () => toastMocks }))
vi.mock('../../components/ReportTruthBadge', () => ({ default: () => null }))

const customCatalog = [
    { accountCode: '9001', name: 'Harbor Vault', nameAr: 'خزينة الميناء', currentBalance: 400, accountType: 'Cashbox', isClearing: false, preventNegative: true },
    { accountCode: '9002', name: 'Harbor Bank', nameAr: '', currentBalance: 490, accountType: 'Bank', isClearing: false, preventNegative: false },
    { accountCode: '9003', name: 'Harbor Clearing', nameAr: 'مقاصة الميناء', currentBalance: 55, accountType: 'Clearing', isClearing: true, preventNegative: false },
    { accountCode: '9020', name: 'Unmapped Asset', nameAr: '', currentBalance: 9999, accountType: 'Asset', isClearing: false, preventNegative: false },
]

describe('TreasuryHub catalog authority', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        langState.language = 'en'
        treasuryApiMock.getTreasuryAccounts.mockResolvedValue(customCatalog)
        treasuryApiMock.getTreasuryDashboard.mockResolvedValue({ netProfit: 10 })
    })

    it('renders cards and totals from arbitrary custom codes and accountType', async () => {
        render(<TreasuryHub />)
        await waitFor(() => expect(treasuryApiMock.getTreasuryAccounts).toHaveBeenCalled())
        expect(await screen.findByText('Harbor Vault')).toBeInTheDocument()
        expect(await screen.findByText('Harbor Bank')).toBeInTheDocument()
        expect(await screen.findByText('Harbor Clearing')).toBeInTheDocument()
        // Available Cashbox + Bank = 890; clearing stays separate; unmapped asset excluded.
        expect(screen.getByText('EGP 890.00')).toBeInTheDocument()
        expect(screen.getAllByText('EGP 55.00').length).toBeGreaterThanOrEqual(1)
        expect(screen.queryByText('Unmapped Asset')).not.toBeInTheDocument()
        expect(screen.queryByText('EGP 9999.00')).not.toBeInTheDocument()

        const availableSection = screen.getByRole('heading', { name: /Available now/ }).closest('section')
        const clearingSection = screen.getByRole('heading', { name: /In transit/ }).closest('section')
        expect(availableSection).not.toBeNull()
        expect(clearingSection).not.toBeNull()
        expect(within(availableSection!).getByText('Harbor Vault')).toBeInTheDocument()
        expect(within(availableSection!).queryByText('Harbor Clearing')).not.toBeInTheDocument()
        expect(within(clearingSection!).getByText('Harbor Clearing')).toBeInTheDocument()
        expect(within(clearingSection!).queryByText('Harbor Vault')).not.toBeInTheDocument()
    })

    it('uses Arabic server names with English fallback', async () => {
        langState.language = 'ar-EG'
        render(<TreasuryHub />)
        await waitFor(() => expect(treasuryApiMock.getTreasuryAccounts).toHaveBeenCalled())
        expect(await screen.findByText('خزينة الميناء')).toBeInTheDocument()
        expect(await screen.findByText('Harbor Bank')).toBeInTheDocument()
        expect(await screen.findByText('مقاصة الميناء')).toBeInTheDocument()
    })

    it('renders an honest empty state for an empty catalog with zero totals', async () => {
        treasuryApiMock.getTreasuryAccounts.mockResolvedValue([])
        render(<TreasuryHub />)
        await waitFor(() => expect(treasuryApiMock.getTreasuryAccounts).toHaveBeenCalled())
        expect((await screen.findAllByText('EGP 0.00')).length).toBeGreaterThanOrEqual(2)
        expect(screen.getByText('No Treasury accounts are mapped for this tenant.')).toBeInTheDocument()
        expect(screen.getByText(/Ask an Owner to map/)).toBeInTheDocument()
        expect(screen.queryByText('Harbor Vault')).not.toBeInTheDocument()
    })
})

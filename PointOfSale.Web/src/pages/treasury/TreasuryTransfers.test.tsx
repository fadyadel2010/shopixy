import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import TreasuryTransfers from './TreasuryTransfers.jsx'

const treasuryApiMock = vi.hoisted(() => ({ getTreasuryAccounts: vi.fn() }))
const navigateMock = vi.hoisted(() => vi.fn())
const toastMocks = vi.hoisted(() => ({ showError: vi.fn(), showSuccess: vi.fn() }))
const translateMock = vi.hoisted(() => (key: string, fallback?: string) => fallback || key)

vi.mock('../../services/treasuryApi', () => treasuryApiMock)
vi.mock('../../services/api', () => ({
    default: { get: vi.fn(), post: vi.fn() },
    isRequestCanceled: () => false,
}))
vi.mock('react-router-dom', () => ({
    useNavigate: () => navigateMock,
    useLocation: () => ({ state: null }),
}))
vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: translateMock, i18n: { language: 'en' } }),
}))
vi.mock('../../contexts/SettingsContext', () => ({
    useSettings: () => ({ formatCurrency: (v: number) => `EGP ${Number(v).toFixed(2)}` }),
}))
vi.mock('../../components/Toast', () => ({ useToast: () => toastMocks }))
vi.mock('../../components/ReportTruthBadge', () => ({ default: () => null }))

describe('TreasuryTransfers catalog boundary', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        treasuryApiMock.getTreasuryAccounts.mockResolvedValue([
            { accountCode: '9001', name: 'Harbor Vault', nameAr: '', currentBalance: 400, accountType: 'Cashbox' },
            { accountCode: '9002', name: 'Harbor Bank', nameAr: '', currentBalance: 100, accountType: 'Bank' },
            { accountCode: '9003', name: 'Harbor Clearing', nameAr: '', currentBalance: 55, accountType: 'Clearing' },
            { accountCode: '9020', name: 'Mystery Asset', nameAr: '', currentBalance: 10, accountType: 'Mystery' },
        ])
    })

    it('offers only exact Cashbox/Bank rows and excludes Clearing/unknown rows', async () => {
        render(<TreasuryTransfers />)
        await waitFor(() => expect(treasuryApiMock.getTreasuryAccounts).toHaveBeenCalled())
        const selectors = await screen.findAllByRole('combobox')
        expect(selectors).toHaveLength(2)
        for (const selector of selectors) {
            const labels = within(selector).getAllByRole('option').map(option => option.textContent || '')
            expect(labels.some(label => label.includes('9001') && label.includes('[Cashbox]'))).toBe(true)
            expect(labels.some(label => label.includes('9002') && label.includes('[Bank]'))).toBe(true)
            expect(labels.some(label => label.includes('9003'))).toBe(false)
            expect(labels.some(label => label.includes('9020'))).toBe(false)
        }
    })
})

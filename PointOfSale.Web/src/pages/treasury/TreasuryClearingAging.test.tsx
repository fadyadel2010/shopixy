import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import TreasuryClearingAging from './TreasuryClearingAging.jsx'

const finApiMock = vi.hoisted(() => ({ getClearingAging: vi.fn() }))
const navigateMock = vi.hoisted(() => vi.fn())
const toastMocks = vi.hoisted(() => ({ showError: vi.fn(), showSuccess: vi.fn() }))
const translateMock = vi.hoisted(() => (key: string, fallback?: string) => fallback || key)

vi.mock('../../services/financialReportsApi', () => finApiMock)
vi.mock('react-router-dom', () => ({ useNavigate: () => navigateMock }))
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
    formatDateTime: (d: unknown) => String(d),
    parseDateInput: () => new Date(2026, 8, 16),
}))

describe('TreasuryClearingAging evidence boundary', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        finApiMock.getClearingAging.mockResolvedValue({
            totalPending: 55,
            accountsWithBalance: 1,
            oldestAgeDays: 9,
            lines: [{
                accountId: 'c1', accountCode: '9003', accountName: 'Harbor Clearing',
                pendingAmount: 55, ageDays: 9, transactionCount: 2,
                current: 10, days8To14: 45, days15To30: 0, over30Days: 0,
            }],
        })
    })

    it('keeps the action disabled with the evidence-required boundary and no settlement path', async () => {
        render(<TreasuryClearingAging />)
        await waitFor(() => expect(finApiMock.getClearingAging).toHaveBeenCalled())
        const action = await screen.findByRole('button', { name: /Provider workflow/ })
        expect(action).toBeDisabled()
        expect(action.title).toMatch(/provider or carrier evidence/i)
        expect(screen.getByText(/Manual transfer or settlement from this screen is not possible/)).toBeInTheDocument()
    })
})

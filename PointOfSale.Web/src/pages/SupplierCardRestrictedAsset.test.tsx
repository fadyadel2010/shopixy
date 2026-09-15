import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import SupplierCardFundingInstruments from './SupplierCardFundingInstruments.jsx'

const cardApiMock = vi.hoisted(() => ({
    list: vi.fn(),
    listFundingSources: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    fund: vi.fn(),
    statement: vi.fn(),
}))
const toastMocks = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))

vi.mock('../services/supplierCardFundingApi', async (importOriginal) => ({
    ...await importOriginal<typeof import('../services/supplierCardFundingApi')>(),
    supplierCardFundingApi: cardApiMock,
}))
vi.mock('../store/authStore', () => ({
    useAuthStore: (selector: (state: unknown) => unknown) => selector({ user: { role: 'Manager' } }),
}))
vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: (_k: string, fallback?: string) => fallback || _k, i18n: { language: 'en' } }),
}))
vi.mock('../components/Toast', () => ({ useToast: () => toastMocks }))
vi.mock('../contexts/SettingsContext', () => ({
    useSettings: () => ({ formatCurrency: (value: number) => `EGP ${Number(value).toFixed(2)}` }),
}))
vi.mock('../hooks/useRTL', () => ({ useRTL: () => ({ isRTL: false, dir: 'ltr' }) }))

describe('SupplierCardFundingInstruments restricted asset copy', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        cardApiMock.list.mockResolvedValue([])
        cardApiMock.listFundingSources.mockResolvedValue([])
    })

    it('states restricted non-cash semantics', async () => {
        render(<SupplierCardFundingInstruments />)
        await waitFor(() => expect(cardApiMock.list).toHaveBeenCalled())
        expect(await screen.findByText(/Restricted card funding for supplier purchases and payments only/)).toBeInTheDocument()
        expect(screen.getByText(/not a Treasury transfer destination/)).toBeInTheDocument()
    })
})

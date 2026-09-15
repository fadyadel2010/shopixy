import { StrictMode } from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import PurchaseInvoice from './PurchaseInvoice.jsx'

const apiGetMock = vi.hoisted(() => vi.fn())
const showErrorMock = vi.hoisted(() => vi.fn())
const translateMock = vi.hoisted(() => (key: string) => key)
let cardInstrumentResponse: unknown[] = []

vi.mock('../services/api', () => ({
    default: {
        get: apiGetMock,
        post: vi.fn(),
        put: vi.fn(),
    },
    isRequestCanceled: () => false,
}))

vi.mock('../components/Toast', () => ({
    useToast: () => ({ showSuccess: vi.fn(), showError: showErrorMock }),
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: translateMock,
        i18n: { language: 'en' },
    }),
}))

vi.mock('../contexts/SettingsContext', () => ({
    useSettings: () => ({
        settings: { currencyCode: 'EGP' },
        formatCurrency: (value: number) => `EGP ${value.toFixed(2)}`,
    }),
}))

vi.mock('../hooks/useRTL', () => ({
    useRTL: () => ({ isRTL: false, dir: 'ltr' }),
}))

vi.mock('../components/AsyncEntitySelect', () => ({
    default: () => <div data-testid="supplier-select" />,
}))

vi.mock('../components/InvoiceSearchModal', () => ({ default: () => null }))
vi.mock('../components/PurchasePrintModal', () => ({ default: () => null }))
vi.mock('../components/VariantSelectModal', () => ({ default: () => null }))

describe('PurchaseInvoice', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        showErrorMock.mockReset()
        cardInstrumentResponse = []
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/warehouses') {
                return Promise.resolve({
                    data: [{ id: 'warehouse-1', name: 'Main Warehouse', isDefault: true }],
                })
            }

            if (url === '/supplier-card-funding-instruments') {
                return Promise.resolve({ data: cardInstrumentResponse })
            }

            if (url === '/products/search') {
                return Promise.resolve({
                    data: [{
                        id: 'product-1',
                        name: 'Samsung TV',
                        barcode: '123',
                        unit: 'Piece',
                        taxRate: 10,
                        hasVariants: false,
                        variants: [{
                            id: 'variant-1',
                            sku: 'TV-1',
                            costPrice: 50,
                            price: 100,
                        }],
                        warehouseStocks: [{ warehouseId: 'warehouse-1', quantity: 4 }],
                    }],
                })
            }

            if (url === '/products/product-1/units') {
                return Promise.resolve({
                    data: [{
                        unitId: 'unit-1',
                        unitName: 'Piece',
                        conversionFactor: 1,
                        purchasePrice: 50,
                        isDefault: true,
                    }],
                })
            }

            return Promise.reject(new Error(`Unexpected GET ${url}`))
        })
    })

    it('adds a searched product after the StrictMode effect replay', async () => {
        render(
            <StrictMode>
                <PurchaseInvoice />
            </StrictMode>,
        )

        const searchInput = await screen.findByPlaceholderText('purchases.scanPlaceholder')
        await waitFor(() => expect(searchInput).not.toBeDisabled())

        fireEvent.change(searchInput, { target: { value: 'samsung-tv-50' } })

        const result = await screen.findByRole('button', { name: /Samsung TV/ }, { timeout: 2000 })
        fireEvent.click(result)

        await waitFor(() => {
            expect(apiGetMock).toHaveBeenCalledWith(
                '/products/product-1/units',
                expect.objectContaining({ signal: expect.any(AbortSignal) }),
            )
        })
        expect(await screen.findByText('Samsung TV')).toBeInTheDocument()
        expect(screen.queryByText('purchases.noItems')).not.toBeInTheDocument()
        expect(showErrorMock).not.toHaveBeenCalled()
    })

    it('does not silently select the first supplier card funding instrument', async () => {
        cardInstrumentResponse = [{
            id: 'instrument-1',
            displayName: 'Company Visa',
            last4: '4521',
            authorityVersion: 2,
            availableBalance: 100,
            fundingSourceName: 'Main Bank',
        }]
        render(<PurchaseInvoice />)

        const cardOption = await screen.findByRole('option', { name: 'purchases.card' })
        fireEvent.change(cardOption.closest('select')!, { target: { value: 'Card' } })
        const instrumentOption = await screen.findByRole('option', { name: /Company Visa/ })

        expect(instrumentOption.closest('select')).toHaveValue('')
        expect(screen.queryByText('purchases.noCardInstruments')).not.toBeInTheDocument()
    })

    it('shows a clear blocked state when Card has no active instrument', async () => {
        render(<PurchaseInvoice />)
        const cardOption = await screen.findByRole('option', { name: 'purchases.card' })
        fireEvent.change(cardOption.closest('select')!, { target: { value: 'Card' } })
        expect(await screen.findByText('purchases.noCardInstruments')).toHaveAttribute('role', 'alert')
    })
})

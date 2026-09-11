import { StrictMode } from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import PurchaseInvoice from './PurchaseInvoice.jsx'

const apiGetMock = vi.hoisted(() => vi.fn())
const showErrorMock = vi.hoisted(() => vi.fn())
const translateMock = vi.hoisted(() => (key: string) => key)

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
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/warehouses') {
                return Promise.resolve({
                    data: [{ id: 'warehouse-1', name: 'Main Warehouse', isDefault: true }],
                })
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
})

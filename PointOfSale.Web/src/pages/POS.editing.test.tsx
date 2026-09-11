import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import POS from './POS.jsx'

const apiGetMock = vi.hoisted(() => vi.fn())
const confirmMock = vi.hoisted(() => vi.fn())
const clearPersistedPosCartMock = vi.hoisted(() => vi.fn())
const updateLocalSettingsMock = vi.hoisted(() => vi.fn())
const showSuccessMock = vi.hoisted(() => vi.fn())
const showErrorMock = vi.hoisted(() => vi.fn())
const showInfoMock = vi.hoisted(() => vi.fn())
const showWarningMock = vi.hoisted(() => vi.fn())

const warehouseId = '22222222-2222-4222-8222-222222222222'
const productId = '33333333-3333-4333-8333-333333333333'
const variantId = '44444444-4444-4444-8444-444444444444'
const unitId = '55555555-5555-4555-8555-555555555555'

const translate = vi.hoisted(() => {
    const translations: Record<string, string> = {
        'common.cancel': 'Cancel',
        'common.editing': 'Editing',
        'common.search': 'Search',
        'common.unit': 'Unit',
        'pos.cartEmpty': 'Cart is empty',
        'pos.discardAndExit': 'Discard and Exit',
        'pos.exitEditing': 'Exit Editing',
        'pos.exitEditingConfirm': 'Discard changes and exit editing?',
        'pos.exitEditingTitle': 'Exit invoice editing?',
        'pos.invoiceDiscountType': 'Invoice discount type',
        'pos.invoiceDiscountValue': 'Invoice discount value',
        'pos.processPayment': 'Process Payment',
        'pos.title': 'Point of Sale',
    }
    return (key: string) => translations[key] ?? key
})

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: translate,
        i18n: { language: 'en' },
    }),
}))

vi.mock('../services/api', () => ({
    default: { get: apiGetMock },
    isRequestCanceled: () => false,
}))

vi.mock('../common/confirm/ConfirmDialogProvider', () => ({
    useConfirmDialog: () => ({ confirm: confirmMock }),
}))

vi.mock('../contexts/SettingsContext', () => ({
    useSettings: () => ({
        formatCurrency: (value: number) => `EGP ${value.toFixed(2)}`,
        settings: { printFormat: 'receipt' },
        updateLocalSettings: updateLocalSettingsMock,
    }),
}))

vi.mock('../components/Toast', () => ({
    useToast: () => ({
        showSuccess: showSuccessMock,
        showError: showErrorMock,
        showInfo: showInfoMock,
        showWarning: showWarningMock,
    }),
}))

vi.mock('../utils/posCartStorage', () => ({
    buildPosCartSnapshotFingerprint: (snapshot: unknown) => JSON.stringify(snapshot),
    clearPersistedPosCart: clearPersistedPosCartMock,
    getPersistedPosCart: () => null,
    savePosCartSnapshot: vi.fn(),
    shouldApplyExternalPosCartSnapshot: () => true,
    subscribeToPosCartChanges: () => () => {},
}))

vi.mock('../services/posStockService', () => ({
    loadPosItemStock: vi.fn(),
    revalidatePosCartStock: vi.fn().mockResolvedValue({
        ok: true,
        mismatches: [],
        effectiveStockLevels: [{
            productId: '33333333-3333-4333-8333-333333333333',
            variantId: '44444444-4444-4444-8444-444444444444',
            stockQuantity: 11,
        }],
    }),
}))

vi.mock('../services/productUnitsApi', () => ({
    getProductUnitsForProduct: () => [],
    loadProductUnitsMap: vi.fn().mockResolvedValue(new Map()),
}))

vi.mock('../services/posCartApi', () => ({
    cancelPosCart: vi.fn(),
    isPosCartId: () => false,
    synchronizePosCart: vi.fn(),
}))

vi.mock('../services/posRuntimeCache', () => ({
    invalidatePosRuntimeCache: vi.fn(),
}))

vi.mock('../components/BarcodeScanner', () => ({ default: () => <div /> }))
vi.mock('../components/ProductSearch', () => ({ default: () => <div /> }))
vi.mock('../components/QuickProductGrid', () => ({ default: () => <div /> }))
vi.mock('../components/PaymentModal', () => ({ default: () => <div /> }))
vi.mock('../components/ReceiptModal', () => ({ default: () => <div /> }))
vi.mock('../components/CustomerSelectModal', () => ({ default: () => <div /> }))
vi.mock('../components/HoldSalesModal', () => ({ default: () => <div /> }))
vi.mock('../components/VariantSelectModal', () => ({ default: () => <div /> }))
vi.mock('../components/CartItem', () => ({ default: () => <div>Draft line</div> }))
vi.mock('../components/InvoiceSearchModal', () => ({
    default: ({ onSelect }: { onSelect: (invoice: { id: string }) => void }) => (
        <button type="button" onClick={() => onSelect({ id: 'draft-id' })}>Load Draft</button>
    ),
}))

const draftSale = {
    id: 'draft-id',
    invoiceNumber: 'INV-DRAFT-1',
    status: 'Draft',
    mutationVersion: 3,
    paymentMethod: 'Cash',
    paidAmount: 110,
    cashAmount: 110,
    discountPercentage: 0,
    discountAmount: 0,
    warehouseId,
    customer: { id: 'customer-id', name: 'Cash Customer' },
    items: [{
        productId,
        productVariantId: variantId,
        productName: 'Draft Product',
        quantity: 1,
        unitId,
        unit: { name: 'Piece' },
        conversionFactor: 1,
        unitPrice: 100,
        discountAmount: 0,
        taxRate: 10,
        priceIncludesTax: false,
    }],
}

const enterEditMode = async () => {
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Search' }))
    await user.click(screen.getByRole('button', { name: 'Load Draft' }))
    await screen.findByText('Editing: #INV-DRAFT-1')
    return user
}

describe('POS edit-mode exit', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        apiGetMock.mockImplementation(async (url: string) => {
            if (url === '/warehouses') {
                return { data: [{ id: warehouseId, name: 'Main Warehouse', isDefault: true }] }
            }
            if (url === '/sales/draft-id') {
                return { data: draftSale }
            }
            throw new Error(`Unexpected GET ${url}`)
        })
    })

    it('exits immediately without confirmation when the edit state is unchanged', async () => {
        render(<POS />)
        const user = await enterEditMode()

        await user.click(screen.getByRole('button', { name: 'Exit Editing' }))

        await waitFor(() => expect(screen.queryByText('Editing: #INV-DRAFT-1')).not.toBeInTheDocument())
        expect(screen.getByText('Cart is empty')).toBeInTheDocument()
        expect(confirmMock).not.toHaveBeenCalled()
        expect(clearPersistedPosCartMock).toHaveBeenCalledOnce()
        expect(apiGetMock).toHaveBeenCalledTimes(2)
    })

    it('keeps dirty edit state on cancel and discards only frontend state on confirm', async () => {
        confirmMock.mockResolvedValueOnce(false).mockResolvedValueOnce(true)
        render(<POS />)
        const user = await enterEditMode()

        fireEvent.change(screen.getByRole('spinbutton', { name: 'Invoice discount value' }), {
            target: { value: '5' },
        })
        await user.click(screen.getByRole('button', { name: 'Exit Editing' }))

        expect(confirmMock).toHaveBeenLastCalledWith(expect.objectContaining({
            description: 'Discard changes and exit editing?',
            variant: 'warning',
        }))
        expect(screen.getByText('Editing: #INV-DRAFT-1')).toBeInTheDocument()

        await user.click(screen.getByRole('button', { name: 'Exit Editing' }))

        await waitFor(() => expect(screen.queryByText('Editing: #INV-DRAFT-1')).not.toBeInTheDocument())
        expect(screen.getByText('Cart is empty')).toBeInTheDocument()
        expect(confirmMock).toHaveBeenCalledTimes(2)
        expect(clearPersistedPosCartMock).toHaveBeenCalledOnce()
        expect(apiGetMock).toHaveBeenCalledTimes(2)
    })
})

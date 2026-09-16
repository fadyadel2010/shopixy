import i18next, { type i18n as I18nInstance } from 'i18next'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { I18nextProvider, initReactI18next } from 'react-i18next'
import ProductVariantsTab from './ProductVariantsTab'
import en from '../i18n/locales/en.json'
import ar from '../i18n/locales/ar.json'

const apiGetMock = vi.hoisted(() => vi.fn())

vi.mock('../services/api', () => ({
    default: {
        get: apiGetMock,
        post: vi.fn(),
        put: vi.fn(),
        delete: vi.fn(),
    },
}))

vi.mock('../common/confirm/ConfirmDialogProvider', () => ({
    useConfirmDialog: () => ({ confirm: vi.fn().mockResolvedValue(true) }),
}))

vi.mock('../services/posRuntimeCache', () => ({
    invalidatePosRuntimeCache: vi.fn(),
}))

async function createI18n(lng: string): Promise<I18nInstance> {
    const instance = i18next.createInstance()
    instance.use(initReactI18next)
    await instance.init({
        resources: {
            en: { translation: en },
            ar: { translation: ar },
        },
        lng,
        fallbackLng: 'en',
        returnNull: false,
        returnEmptyString: false,
        interpolation: { escapeValue: false },
    })
    return instance
}

function mockReads() {
    apiGetMock.mockImplementation((url: string) => {
        if (url === '/products/product-1/variants') return Promise.resolve({ data: [] })
        if (url === '/admin/products/product-1/media') return Promise.resolve({ data: [] })
        if (url.includes('/variants/') && url.endsWith('/can-delete')) {
            return Promise.resolve({ data: { canDelete: true, reasons: [] } })
        }
        return Promise.resolve({ data: [] })
    })
}

async function renderWithLocale(lng: string) {
    const instance = await createI18n(lng)
    return render(
        <I18nextProvider i18n={instance}>
            <ProductVariantsTab
                productId="product-1"
                forceMatrixMode
                productSkuPrefix="T Shirt"
                onSaveVariantDraft={vi.fn().mockResolvedValue({ id: 'product-1' })}
            />
        </I18nextProvider>
    )
}

const ENGLISH_BUSINESS_WORDS = [
    'Variant Templates',
    'Choose Attributes',
    'SKU Builder',
    'Workspace Statistics',
    'Combination Rules',
    'Variant Options',
    'Generate Variants',
    'Bulk Actions',
    'Identity Tools',
    'Advanced Variant Tools',
    'Variants to Generate',
    'Select Variant Image',
]

describe('Wave 4 Arabic RTL rendering (real locale)', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        mockReads()
    })

    it('renders the primary workflow fully translated with no English business leakage', async () => {
        const { container } = await renderWithLocale('ar')

        expect(await screen.findByText('خيارات المتغير')).toBeInTheDocument()
        expect(screen.getByText('إنشاء المتغيرات')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: /أدوات المتغيرات المتقدمة/ })).toBeInTheDocument()

        const bodyText = container.textContent || ''
        for (const word of ENGLISH_BUSINESS_WORDS) {
            expect(bodyText).not.toContain(word)
        }
        // Technical identifiers stay intact.
        expect(bodyText).toContain('SKU')
    })

    it('renders Arabic advanced sections, matrix headers, and bulk tools on expand', async () => {
        const { container } = await renderWithLocale('ar')

        fireEvent.click(await screen.findByRole('button', { name: /أدوات المتغيرات المتقدمة/ }))

        expect(await screen.findByText('قوالب المتغيرات')).toBeInTheDocument()
        expect(screen.getByText('اختيار الخصائص')).toBeInTheDocument()
        expect(screen.getByText('منشئ SKU')).toBeInTheDocument()
        expect(screen.getByText('قواعد التركيبات')).toBeInTheDocument()
        expect(screen.getByText('إحصائيات مساحة العمل')).toBeInTheDocument()
        // Localized preset + template labels (canonical values stay English-safe).
        expect(screen.getByText('اللون')).toBeInTheDocument()
        expect(screen.getByText('تيشيرت')).toBeInTheDocument()
        expect(screen.getByText('إجراءات جماعية')).toBeInTheDocument()

        const bodyText = container.textContent || ''
        for (const word of ENGLISH_BUSINESS_WORDS) {
            expect(bodyText).not.toContain(word)
        }
    })

    it('builds a readable Arabic-context variant name from user data without translating it', async () => {
        const { container } = await renderWithLocale('ar')

        fireEvent.click(await screen.findByRole('button', { name: /إضافة مجموعة خيارات/ }))
        const nameInputs = Array.from(container.querySelectorAll('input[placeholder="مثال: المقاس، اللون، الخامة"]')) as HTMLInputElement[]
        fireEvent.change(nameInputs[nameInputs.length - 1], { target: { value: 'مقاس' } })

        const valueInputs = Array.from(container.querySelectorAll('input[placeholder="اكتب قيمة واضغط Enter"]')) as HTMLInputElement[]
        const target = valueInputs[valueInputs.length - 1]
        fireEvent.change(target, { target: { value: '256' } })
        fireEvent.keyDown(target, { key: 'Enter' })

        const table = container.querySelector('table')
        expect(table).not.toBeNull()
        expect(within(table as HTMLTableElement).getAllByText('مقاس: 256').length).toBeGreaterThan(0)
    })

    it('keeps responsive/ARIA structure intact in Arabic', async () => {
        const { container } = await renderWithLocale('ar')

        const toggle = await screen.findByRole('button', { name: /أدوات المتغيرات المتقدمة/ })
        expect(toggle).toHaveAttribute('type', 'button')
        expect(toggle).toHaveAttribute('aria-expanded', 'false')
        expect(toggle).toHaveAttribute('aria-controls', 'variant-advanced-tools')
    })

    it('announces the localized server reason and names the protected variant after Save', async () => {
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/products/product-1/variants') {
                return Promise.resolve({
                    data: ['Plain', 'Medium', 'Dark'].map((value, index) => ({
                        id: `variant-${value.toLowerCase()}`,
                        productId: 'product-1',
                        sku: `STYLE-${value.toUpperCase()}`,
                        barcode: `300${index}`,
                        price: 10,
                        costPrice: 5,
                        isDefault: value === 'Plain',
                        isActive: true,
                        attributes: [{ name: 'Style', value, sortOrder: 0 }],
                    })),
                })
            }
            if (url === '/products/product-1/variants/variant-dark/can-delete') {
                return Promise.resolve({
                    data: {
                        canDelete: false,
                        errorCode: 'VARIANT_HAS_STOCK',
                        messageEn: 'Cannot delete this variant: current stock exists in warehouses.',
                        messageAr: 'لا يمكن حذف هذا المتغير: يوجد رصيد حالي بالمخازن.',
                        reasons: [{
                            code: 'VARIANT_HAS_STOCK',
                            messageEn: 'Cannot delete this variant: current stock exists in warehouses.',
                            messageAr: 'لا يمكن حذف هذا المتغير: يوجد رصيد حالي بالمخازن.',
                        }],
                    },
                })
            }
            return Promise.resolve({ data: [] })
        })

        const { container } = await renderWithLocale('ar')
        expect((await screen.findAllByText('Dark')).length).toBeGreaterThan(0)

        const darkChip = Array.from(container.querySelectorAll('span')).find(
            (element) => element.textContent?.startsWith('Dark') && element.querySelector('button')
        ) as HTMLElement
        fireEvent.click(darkChip.querySelector('button') as HTMLButtonElement)

        const valueInput = container.querySelector(
            'input[placeholder="اكتب قيمة واضغط Enter"]'
        ) as HTMLInputElement
        fireEvent.change(valueInput, { target: { value: 'Mojahed' } })
        fireEvent.keyDown(valueInput, { key: 'Enter' })

        const numericInputs = Array.from(container.querySelectorAll('table input[type="number"]')) as HTMLInputElement[]
        fireEvent.change(numericInputs[8], { target: { value: '5' } })
        fireEvent.change(numericInputs[9], { target: { value: '10' } })
        fireEvent.click(screen.getByRole('button', { name: 'حفظ المصفوفة' }))

        const alert = await screen.findByRole('alert')
        expect(alert).toHaveTextContent('لم يتم حفظ مصفوفة المتغيرات.')
        expect(alert).toHaveTextContent('Style: Dark (STYLE-DARK): لا يمكن حذف هذا المتغير: يوجد رصيد حالي بالمخازن.')
        expect(alert).not.toHaveTextContent('Cannot delete')
        expect(alert).toHaveFocus()
    })
})

describe('Wave 4 English LTR rendering (real locale)', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        mockReads()
    })

    it('renders the same workflow in correct English with no raw keys', async () => {
        const { container } = await renderWithLocale('en')

        expect(await screen.findByText('Variant Options')).toBeInTheDocument()
        expect(screen.getByText('Generate Variants')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: /Advanced Variant Tools/ })).toBeInTheDocument()

        fireEvent.click(screen.getByRole('button', { name: /Advanced Variant Tools/ }))
        expect(await screen.findByText('Variant Templates')).toBeInTheDocument()
        expect(screen.getByText('SKU Builder')).toBeInTheDocument()

        const bodyText = container.textContent || ''
        expect(bodyText).not.toMatch(/variants\.[a-zA-Z]+/)
        expect(bodyText).not.toMatch(/common\.[a-zA-Z]+/)
    })

    it('keeps the matrix horizontally scrollable for narrow screens', async () => {
        const { container } = await renderWithLocale('en')

        fireEvent.click(await screen.findByRole('button', { name: /Add Option Group/ }))
        const nameInputs = Array.from(container.querySelectorAll('input[placeholder="e.g. Size, Color, Material"]')) as HTMLInputElement[]
        fireEvent.change(nameInputs[nameInputs.length - 1], { target: { value: 'Size' } })
        const valueInputs = Array.from(container.querySelectorAll('input[placeholder="Type a value and press Enter"]')) as HTMLInputElement[]
        const target = valueInputs[valueInputs.length - 1]
        fireEvent.change(target, { target: { value: '256' } })
        fireEvent.keyDown(target, { key: 'Enter' })

        const table = container.querySelector('table')
        expect(table).not.toBeNull()
        expect((table as HTMLTableElement).closest('div.overflow-x-auto')).not.toBeNull()
        expect(within(table as HTMLTableElement).getAllByText('Size: 256').length).toBeGreaterThan(0)
    })
})

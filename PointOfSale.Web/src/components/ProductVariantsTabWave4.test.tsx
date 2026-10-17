import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import ProductVariantsTab from './ProductVariantsTab'
import en from '../i18n/locales/en.json'
import ar from '../i18n/locales/ar.json'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiPostMock = vi.hoisted(() => vi.fn())
const apiPutMock = vi.hoisted(() => vi.fn())
const apiDeleteMock = vi.hoisted(() => vi.fn())
const saveVariantDraftMock = vi.hoisted(() => vi.fn())

vi.mock('../services/api', () => ({
    default: {
        get: apiGetMock,
        post: apiPostMock,
        put: apiPutMock,
        delete: apiDeleteMock,
    },
}))

vi.mock('../common/confirm/ConfirmDialogProvider', () => ({
    useConfirmDialog: () => ({ confirm: vi.fn().mockResolvedValue(true) }),
}))

vi.mock('../services/posRuntimeCache', () => ({
    invalidatePosRuntimeCache: vi.fn(),
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string) => key,
        i18n: { language: 'en' },
    }),
}))

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

function renderVariantTab(props = {}) {
    return render(
        <ProductVariantsTab
            productId="product-1"
            forceMatrixMode
            productSkuPrefix="T Shirt"
            onSaveVariantDraft={saveVariantDraftMock}
            {...props}
        />
    )
}

async function openAdvanced() {
    const toggle = await screen.findByRole('button', { name: /Advanced Variant Tools/ })
    if (toggle.getAttribute('aria-expanded') === 'false') {
        fireEvent.click(toggle)
    }
    await screen.findByText('Variant Templates')
}

async function addOptionValue(container: HTMLElement, inputIndex: number, value: string) {
    const valueInputs = Array.from(container.querySelectorAll('input[placeholder="Type a value and press Enter"]')) as HTMLInputElement[]
    fireEvent.change(valueInputs[inputIndex], { target: { value } })
    fireEvent.keyDown(valueInputs[inputIndex], { key: 'Enter' })
}

async function addOptionGroupViaPrimary(container: HTMLElement, name: string) {
    fireEvent.click(await screen.findByRole('button', { name: /Add Option Group/ }))
    const nameInputs = Array.from(container.querySelectorAll('input[placeholder="e.g. Size, Color, Material"]')) as HTMLInputElement[]
    fireEvent.change(nameInputs[nameInputs.length - 1], { target: { value: name } })
}

function fillRequiredPrices(container: HTMLElement) {
    const numericInputs = Array.from(container.querySelectorAll('table input[type="number"]')) as HTMLInputElement[]
    for (let row = 0; row < numericInputs.length / 4; row += 1) {
        fireEvent.change(numericInputs[row * 4], { target: { value: '5' } })
        fireEvent.change(numericInputs[row * 4 + 1], { target: { value: '10' } })
    }
}

async function waitForRowName(text: string) {
    await screen.findAllByText(text)
}

function getTable(container: HTMLElement) {
    const table = container.querySelector('table')
    if (!table) throw new Error('expected variant matrix table to be rendered')
    return table
}

function lookup(locale: Record<string, unknown>, key: string) {
    return key.split('.').reduce<unknown>((acc, part) => (
        acc && typeof acc === 'object' ? (acc as Record<string, unknown>)[part] : undefined
    ), locale)
}

const EDITOR_FILES = [
    'ProductVariantsTab.jsx',
    'VariantOptionsBuilder.jsx',
    'VariantMatrixTable.jsx',
    'VariantBulkActions.jsx',
    'VariantMediaPicker.jsx',
]

function editorI18nKeys() {
    const dir = path.dirname(fileURLToPath(import.meta.url))
    const keys = new Set<string>()
    for (const file of EDITOR_FILES) {
        const content = fs.readFileSync(path.join(dir, file), 'utf8')
        const matches = content.matchAll(/\bt[x]?\(\s*['"]([^'"]+)['"]/g)
        for (const match of matches) {
            if (match[1].includes('.')) keys.add(match[1])
        }
    }
    return [...keys].sort()
}

describe('Wave 4 localization', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        apiPostMock.mockReset()
        apiPutMock.mockReset()
        apiDeleteMock.mockReset()
        saveVariantDraftMock.mockReset()
        saveVariantDraftMock.mockResolvedValue({ id: 'product-1' })
        mockReads()
    })

    it('keeps the variants namespace symmetric between en and ar (incl. common.stay)', () => {
        expect(Object.keys(en.variants).sort())
            .toEqual(Object.keys(ar.variants).sort())
        expect(lookup(en as unknown as Record<string, unknown>, 'common.stay')).toBe('Stay')
        expect(lookup(ar as unknown as Record<string, unknown>, 'common.stay')).toBeTruthy()
    })

    it('resolves every editor i18n key in both locales with matching placeholders', () => {
        const keys = editorI18nKeys()
        expect(keys.length).toBeGreaterThan(100)

        const missing: string[] = []
        const placeholderMismatches: string[] = []
        for (const key of keys) {
            const enValue = lookup(en as unknown as Record<string, unknown>, key)
            const arValue = lookup(ar as unknown as Record<string, unknown>, key)
            if (typeof enValue !== 'string' || !enValue) missing.push(`en:${key}`)
            if (typeof arValue !== 'string' || !arValue) missing.push(`ar:${key}`)
            if (typeof enValue === 'string' && typeof arValue === 'string') {
                const placeholders = (value: string) => [...value.matchAll(/\{\{([^}]+)\}\}/g)].map(m => m[1]).sort().join(',')
                if (placeholders(enValue) !== placeholders(arValue)) placeholderMismatches.push(key)
            }
        }

        expect(missing).toEqual([])
        expect(placeholderMismatches).toEqual([])
    })

    it('uses canonical business Arabic terminology for the new variant keys', () => {
        const variantsAr = ar.variants
        expect(variantsAr.optionBuilder).toBe('خيارات المتغير')
        expect(variantsAr.generateVariants).toBe('إنشاء المتغيرات')
        expect(variantsAr.skuBuilder).toBe('منشئ SKU')
        expect(variantsAr.variantName).toBe('اسم المتغير')
        expect(variantsAr.combinationRules).toBe('قواعد التركيبات')
        expect(variantsAr.duplicateSkuShort).toBe('SKU مكرر')
        expect(variantsAr.duplicateCombinationsShort).toBe('تركيبات مكررة')
        expect(variantsAr.weight).toBe('الوزن')
        expect(variantsAr.saveMatrix).toBe('حفظ المصفوفة')
    })
})

describe('Wave 4 variant name semantics (display-only, identity-safe)', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        apiPostMock.mockReset()
        apiPutMock.mockReset()
        apiDeleteMock.mockReset()
        saveVariantDraftMock.mockReset()
        saveVariantDraftMock.mockResolvedValue({ id: 'product-1' })
        mockReads()
    })

    it('renders a descriptive single-attribute name instead of a bare value', async () => {
        const { container } = renderVariantTab()

        await addOptionGroupViaPrimary(container, 'Size')
        await addOptionValue(container, 0, '256')
        await addOptionValue(container, 0, '512')

        const table = getTable(container)
        expect(within(table).getAllByText('Size: 256').length).toBeGreaterThan(0)
        expect(within(table).getAllByText('Size: 512').length).toBeGreaterThan(0)
        expect(within(table).queryByText('256')).toBeNull()
        expect(within(table).queryByText('512')).toBeNull()
    })

    it('renders complete deterministic multi-attribute combination names in generation order', async () => {
        const { container } = renderVariantTab()

        await addOptionGroupViaPrimary(container, 'Color')
        await addOptionValue(container, 0, 'Blue')
        await addOptionValue(container, 0, 'Red')
        await addOptionGroupViaPrimary(container, 'Size')
        await addOptionValue(container, 1, 'S')
        await addOptionValue(container, 1, 'M')

        const table = getTable(container)
        const nameCells = Array.from(table.querySelectorAll('tbody tr td:nth-child(3) > div:first-child')).map(cell => cell.textContent)
        expect(nameCells).toEqual(['Blue / S', 'Blue / M', 'Red / S', 'Red / M'])
    })

    it('keeps the persisted save payload free of display names (locale-safe identity)', async () => {
        const { container } = renderVariantTab()

        await addOptionGroupViaPrimary(container, 'Size')
        await addOptionValue(container, 0, '256')
        fillRequiredPrices(container)
        fireEvent.click(screen.getByRole('button', { name: 'Save Matrix' }))

        await waitFor(() => expect(saveVariantDraftMock).toHaveBeenCalled())
        const payload = saveVariantDraftMock.mock.calls[0][0]
        expect(payload.authoringVariants).toHaveLength(1)
        expect(payload.authoringVariants[0].attributes).toEqual({ Size: '256' })
        expect(payload.authoringVariants[0]).not.toHaveProperty('name')
        expect(payload.authoringVariants[0]).not.toHaveProperty('displayName')
    })

    it('keeps existing persisted variant identity stable when the matrix is extended', async () => {
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/products/product-1/variants') {
                return Promise.resolve({
                    data: [{
                        id: 'variant-plain',
                        productId: 'product-1',
                        sku: 'STYLE-PLAIN',
                        barcode: '3000',
                        price: 10,
                        onlinePrice: null,
                        costPrice: 5,
                        weight: null,
                        isDefault: true,
                        isActive: true,
                        totalStock: 0,
                        attributes: [{ name: 'Style', value: 'Plain', sortOrder: 0 }],
                    }],
                })
            }
            if (url === '/admin/products/product-1/media') return Promise.resolve({ data: [] })
            if (url.includes('/variants/') && url.endsWith('/can-delete')) {
                return Promise.resolve({ data: { canDelete: true, reasons: [] } })
            }
            return Promise.resolve({ data: [] })
        })

        const { container } = renderVariantTab()

        await waitForRowName('Style: Plain')
        await addOptionValue(container, 0, 'Mojahed')
        await waitForRowName('Style: Mojahed')

        fillRequiredPrices(container)
        fireEvent.click(screen.getByRole('button', { name: 'Save Matrix' }))

        await waitFor(() => expect(saveVariantDraftMock).toHaveBeenCalled())
        const payload = saveVariantDraftMock.mock.calls[0][0]
        const byStyle = Object.fromEntries(
            payload.authoringVariants.map((variant: { attributes: { Style: string } }) => [variant.attributes.Style, variant])
        )
        expect(byStyle.Plain.variantId).toBe('variant-plain')
        expect(byStyle.Plain.sku).toBe('STYLE-PLAIN')
        expect(byStyle.Mojahed.variantId).toBeNull()
    })
})

describe('Wave 4 contextual generated SKU (suggestion-only, authority-preserving)', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        apiPostMock.mockReset()
        apiPutMock.mockReset()
        apiDeleteMock.mockReset()
        saveVariantDraftMock.mockReset()
        saveVariantDraftMock.mockResolvedValue({ id: 'product-1' })
        mockReads()
    })

    async function buildSizeMatrix(container: HTMLElement) {
        await addOptionGroupViaPrimary(container, 'Size')
        await addOptionValue(container, 0, '256')
        await addOptionValue(container, 0, '512')
        await waitForRowName('Size: 256')
    }

    function skuInputs(container: HTMLElement) {
        const textInputs = Array.from(container.querySelectorAll('table input[type="text"]')) as HTMLInputElement[]
        return [textInputs[0], textInputs[2]]
    }

    it('never generates a bare value-only SKU for single-attribute values', async () => {
        const { container } = renderVariantTab()
        await buildSizeMatrix(container)

        fireEvent.click(container.querySelector('thead input[type="checkbox"]') as HTMLInputElement)
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU for 2 Selected' }))

        await waitFor(() => {
            const [first, second] = skuInputs(container)
            expect(first.value).toBe('T-SHIRT-256')
            expect(second.value).toBe('T-SHIRT-512')
        })
    })

    it('generates distinct contextual SKUs for multi-attribute combinations', async () => {
        const { container } = renderVariantTab()

        await addOptionGroupViaPrimary(container, 'Color')
        await addOptionValue(container, 0, 'Blue')
        await addOptionValue(container, 0, 'Red')
        await addOptionGroupViaPrimary(container, 'Size')
        await addOptionValue(container, 1, 'S')
        await addOptionValue(container, 1, 'M')
        await screen.findByText('Blue / S')

        fireEvent.click(container.querySelector('thead input[type="checkbox"]') as HTMLInputElement)
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU for 4 Selected' }))

        await waitFor(() => {
            const textInputs = Array.from(container.querySelectorAll('table input[type="text"]')) as HTMLInputElement[]
            const skus = [textInputs[0].value, textInputs[2].value, textInputs[4].value, textInputs[6].value]
            expect(skus).toEqual(['T-SHIRT-BLUE-S', 'T-SHIRT-BLUE-M', 'T-SHIRT-RED-S', 'T-SHIRT-RED-M'])
        })
    })

    it('dedupes normalization collisions with the existing numeric suffix authority', async () => {
        const { container } = renderVariantTab()

        await addOptionGroupViaPrimary(container, 'Color')
        await addOptionValue(container, 0, 'Red Apple')
        await addOptionValue(container, 0, 'Red-Apple')
        await waitForRowName('Color: Red-Apple')

        fireEvent.click(container.querySelector('thead input[type="checkbox"]') as HTMLInputElement)
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU for 2 Selected' }))

        await waitFor(() => {
            const [first, second] = skuInputs(container)
            expect(first.value).toBe('T-SHIRT-RED-APPLE')
            expect(second.value).toBe('T-SHIRT-RED-APPLE-2')
        })
    })

    it('uses an ASCII product-scoped namespace and stable variant ordinals for Arabic names', async () => {
        const { container } = renderVariantTab({ productSkuPrefix: 'منتج تجريبي' })
        await buildSizeMatrix(container)

        fireEvent.click(container.querySelector('thead input[type="checkbox"]') as HTMLInputElement)
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU for 2 Selected' }))

        await waitFor(() => {
            const [first, second] = skuInputs(container)
            expect(first.value).toMatch(/^PX-[0-9A-F]{32}-V01$/)
            expect(second.value).toBe(first.value.replace('-V01', '-V02'))
        })
    })

    it('separates Arabic products even when their option values match', async () => {
        const first = renderVariantTab({ productId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', productSkuPrefix: 'منتج' })
        await buildSizeMatrix(first.container)
        fireEvent.click(first.container.querySelector('thead input[type="checkbox"]') as HTMLInputElement)
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU for 2 Selected' }))
        const firstSku = skuInputs(first.container)[0].value
        first.unmount()

        const second = renderVariantTab({ productId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', productSkuPrefix: 'منتج' })
        await buildSizeMatrix(second.container)
        fireEvent.click(second.container.querySelector('thead input[type="checkbox"]') as HTMLInputElement)
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU for 2 Selected' }))
        const secondSku = skuInputs(second.container)[0].value
        expect(firstSku).toBe('PX-AAAAAAAAAAAA4AAA8AAAAAAAAAAAAAAA-V01')
        expect(secondSku).toBe('PX-BBBBBBBBBBBB4BBB8BBBBBBBBBBBBBBB-V01')
        expect(firstSku).not.toBe(secondSku)
    })

    it('numbers many Arabic variants independently of their Arabic attribute values', async () => {
        const { container } = renderVariantTab({ productSkuPrefix: 'حاسوب' })
        await addOptionGroupViaPrimary(container, 'اللون')
        for (const color of ['أحمر', 'أزرق', 'أسود', 'أبيض', 'أخضر']) {
            await addOptionValue(container, 0, color)
        }
        await waitForRowName('اللون: أخضر')
        fireEvent.click(container.querySelector('thead input[type="checkbox"]') as HTMLInputElement)
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU for 5 Selected' }))
        const skus = (Array.from(container.querySelectorAll('table input[type="text"]')) as HTMLInputElement[])
            .filter((_, index) => index % 2 === 0).map(input => input.value)
        expect(skus.map(sku => sku.slice(-3))).toEqual(['V01', 'V02', 'V03', 'V04', 'V05'])
        expect(new Set(skus).size).toBe(5)
        expect(skus.every(sku => /^[\x20-\x7E]+$/.test(sku))).toBe(true)
    })

    it('does not treat ASCII digits in an Arabic product name as a unique product identity', async () => {
        const { container } = renderVariantTab({ productSkuPrefix: 'منتج 20260924' })
        await buildSizeMatrix(container)
        fireEvent.click(container.querySelector('thead input[type="checkbox"]') as HTMLInputElement)
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU for 2 Selected' }))
        await waitFor(() => expect(skuInputs(container)[0].value).toMatch(/^PX-[0-9A-F]{32}-V01$/))
        expect(skuInputs(container)[1].value).toBe(skuInputs(container)[0].value.replace('-V01', '-V02'))
    })

    it('restores product context for prefix-less custom patterns instead of emitting a bare value', async () => {
        const { container } = renderVariantTab()
        await buildSizeMatrix(container)
        await openAdvanced()

        fireEvent.change(screen.getByPlaceholderText('Pattern, e.g. TSHIRT-{COLOR}-{SIZE}'), {
            target: { value: '{SIZE}' },
        })
        fireEvent.click(container.querySelector('thead input[type="checkbox"]') as HTMLInputElement)
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU for 2 Selected' }))

        await waitFor(() => {
            const [first, second] = skuInputs(container)
            expect(first.value).toBe('T-SHIRT-256')
            expect(second.value).toBe('T-SHIRT-512')
        })
    })

    it('keeps explicitly prefixed patterns byte-identical and suffixes pattern collisions', async () => {
        const { container } = renderVariantTab()
        await buildSizeMatrix(container)
        await openAdvanced()

        fireEvent.change(screen.getByPlaceholderText('Pattern, e.g. TSHIRT-{COLOR}-{SIZE}'), {
            target: { value: 'TSHIRT' },
        })
        expect(screen.getAllByText('TSHIRT').length).toBeGreaterThan(0)
        fireEvent.click(container.querySelector('thead input[type="checkbox"]') as HTMLInputElement)
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU for 2 Selected' }))

        await waitFor(() => {
            const [first, second] = skuInputs(container)
            expect(first.value).toBe('TSHIRT')
            expect(second.value).toBe('TSHIRT-2')
        })
    })

    it('only overwrites explicitly selected rows and preserves existing SKUs', async () => {
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/products/product-1/variants') {
                return Promise.resolve({
                    data: [{
                        id: 'variant-plain',
                        productId: 'product-1',
                        sku: 'STYLE-PLAIN',
                        barcode: '3000',
                        price: 10,
                        onlinePrice: null,
                        costPrice: 5,
                        weight: null,
                        isDefault: true,
                        isActive: true,
                        totalStock: 0,
                        attributes: [{ name: 'Style', value: 'Plain', sortOrder: 0 }],
                    }],
                })
            }
            if (url === '/admin/products/product-1/media') return Promise.resolve({ data: [] })
            if (url.includes('/variants/') && url.endsWith('/can-delete')) {
                return Promise.resolve({ data: { canDelete: true, reasons: [] } })
            }
            return Promise.resolve({ data: [] })
        })

        const { container } = renderVariantTab()
        await waitForRowName('Style: Plain')
        await addOptionValue(container, 0, 'Mojahed')
        await waitForRowName('Style: Mojahed')

        const rowCheckboxes = Array.from(container.querySelectorAll('tbody input[type="checkbox"]')) as HTMLInputElement[]
        // Each matrix row renders a select checkbox followed by a status
        // checkbox, so the second row's selector is index 2.
        fireEvent.click(rowCheckboxes[2])
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU for 1 Selected' }))

        await waitFor(() => {
            const textInputs = Array.from(container.querySelectorAll('table input[type="text"]')) as HTMLInputElement[]
            expect(textInputs[0].value).toBe('STYLE-PLAIN')
            expect(textInputs[2].value).toBe('T-SHIRT-MOJAHED')
        })
    })
})

describe('Wave 4 progressive disclosure UX', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        apiPostMock.mockReset()
        apiPutMock.mockReset()
        apiDeleteMock.mockReset()
        saveVariantDraftMock.mockReset()
        saveVariantDraftMock.mockResolvedValue({ id: 'product-1' })
        mockReads()
    })

    it('completes the primary workflow without ever expanding Advanced tools', async () => {
        const { container } = renderVariantTab()

        const toggle = await screen.findByRole('button', { name: /Advanced Variant Tools/ })
        expect(toggle).toHaveAttribute('aria-expanded', 'false')
        expect(toggle).toHaveAttribute('aria-controls', 'variant-advanced-tools')
        expect(screen.queryByText('Variant Templates')).toBeNull()
        expect(screen.queryByText('SKU Builder')).toBeNull()

        await addOptionGroupViaPrimary(container, 'Size')
        await addOptionValue(container, 0, '256')
        await addOptionValue(container, 0, '512')

        expect(screen.getByRole('button', { name: 'Generate 2 Variants' })).toBeInTheDocument()
        const table = getTable(container)
        expect(within(table).getAllByText('Size: 256').length).toBeGreaterThan(0)
        expect(within(table).getAllByText('Size: 512').length).toBeGreaterThan(0)
        expect(toggle).toHaveAttribute('aria-expanded', 'false')
    })

    it('keeps every advanced capability one disclosure away and collapsible', async () => {
        const { container } = renderVariantTab()
        void container

        const toggle = await screen.findByRole('button', { name: /Advanced Variant Tools/ })
        fireEvent.click(toggle)
        expect(toggle).toHaveAttribute('aria-expanded', 'true')

        const region = document.getElementById('variant-advanced-tools')
        expect(region).not.toBeNull()
        const scoped = within(region as HTMLElement)
        expect(scoped.getByText('Variant Templates')).toBeInTheDocument()
        expect(scoped.getByText('Choose Attributes')).toBeInTheDocument()
        expect(scoped.getByText('SKU Builder')).toBeInTheDocument()
        expect(scoped.getByText('Combination Rules')).toBeInTheDocument()
        expect(scoped.getByText('Workspace Statistics')).toBeInTheDocument()

        fireEvent.click(toggle)
        expect(toggle).toHaveAttribute('aria-expanded', 'false')
        expect(screen.queryByText('Variant Templates')).toBeNull()
    })

    it('keeps the full variant matrix column set on the primary surface', async () => {
        const { container } = renderVariantTab()

        await addOptionGroupViaPrimary(container, 'Size')
        await addOptionValue(container, 0, '256')

        const table = getTable(container)
        const headers = within(table).getAllByRole('columnheader').map(th => th.textContent)
        for (const expected of ['Image', 'Variant Name', 'Attributes', 'SKU', 'Barcode', 'Purchase Price', 'Selling Price', 'Online Price', 'Available Stock', 'Weight', 'Default', 'Status']) {
            expect(headers).toContain(expected)
        }
        expect(table.closest('div.overflow-x-auto')).not.toBeNull()
    })
})

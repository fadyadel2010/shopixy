import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n/config'
import ShippingMethodsTab, { admitAdminShippingMethods, type AdminShippingMethod } from './components/ShippingMethodsTab'

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }))
vi.mock('../../services/api', () => ({ default: api }))
vi.mock('../../common/confirm/ConfirmDialogProvider', () => ({ useConfirmDialog: () => ({ confirm: vi.fn() }) }))
vi.mock('../../contexts/SettingsContext', () => ({
    useSettings: () => ({ settings: { currencySymbol: 'EGP' }, formatCurrency: (value: number) => `${value} EGP` }),
}))

const warehouseId = '33333333-3333-4333-8333-333333333333'
const method: AdminShippingMethod = {
    id: '11111111-1111-4111-8111-111111111111', name: 'Synthetic shared delivery', nameAr: 'شحن تجريبي',
    description: null, baseRate: 25, perKgRate: 2, freeShippingThreshold: 500,
    estimatedDeliveryDaysMin: 1, estimatedDeliveryDaysMax: 5, sortOrder: 0, isActive: true,
    carrierCode: 'manual', carrierName: 'Manual Fulfillment', serviceLevel: 'standard',
    fulfillmentWarehouseId: warehouseId, trackingUrlTemplate: null, availableCountries: '[ "EG", "SA" ]',
}
const warehouses = [{ id: warehouseId, name: 'Synthetic warehouse', code: 'TEST', isActive: true }]
let savedMethods: unknown[]

beforeEach(async () => {
    vi.resetAllMocks()
    await i18n.changeLanguage('en')
    savedMethods = [{ ...method }]
    api.get.mockImplementation(async (url: string) => {
        if (url === '/shippingmethods?activeOnly=false') return { data: savedMethods }
        if (url === '/admin/shipping/supported-carriers') return { data: [{
            carrierCode: 'manual', displayName: 'Manual Fulfillment', isExternal: false,
            accountRequired: false, readinessRequired: false, productionCapability: 'ProductionReady',
            serviceLevels: [{ code: 'standard', name: 'Standard', typicalDaysMin: 1, typicalDaysMax: 5 }],
        }] }
        throw new Error(`Unexpected test request: ${url}`)
    })
    api.put.mockImplementation(async (_url: string, data: Record<string, unknown>) => {
        savedMethods = [{ ...method, ...data }]
        return { data: null }
    })
    api.post.mockImplementation(async (_url: string, data: Record<string, unknown>) => {
        savedMethods = [{ ...method, ...data }]
        return { data: savedMethods[0] }
    })
})
afterEach(async () => {
    cleanup()
    await i18n.changeLanguage('en')
})

const setup = () => render(<ShippingMethodsTab warehouses={warehouses} />)
async function editMethod() {
    setup()
    fireEvent.click(await screen.findByRole('button', { name: 'Edit' }))
    const dialog = screen.getByRole('dialog')
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Update' })).toBeEnabled())
    return within(dialog)
}

describe('shipping method country authoring and readback', () => {
    it('preserves a supported same-day zero estimate and zero free threshold on save', async () => {
        savedMethods = [{ ...method, estimatedDeliveryDaysMin: 0, estimatedDeliveryDaysMax: 0, freeShippingThreshold: 0 }]
        const dialog = await editMethod()
        fireEvent.click(dialog.getByRole('button', { name: 'Update' }))
        await waitFor(() => expect(api.put).toHaveBeenCalledWith(`/shippingmethods/${method.id}`, expect.objectContaining({
            estimatedDeliveryDaysMin: 0, estimatedDeliveryDaysMax: 0, freeShippingThreshold: 0,
        })))
        expect(await screen.findByText('0 EGP+')).toBeVisible()
    })

    it('suppresses same-tick duplicate saves until authoritative readback', async () => {
        let resolve: ((value: unknown) => void) | undefined
        api.put.mockImplementation(() => new Promise(r => { resolve = r }))
        const dialog = await editMethod()
        const form = dialog.getByRole('button', { name: 'Update' }).closest('form')!
        act(() => { fireEvent.submit(form); fireEvent.submit(form) })
        expect(api.put).toHaveBeenCalledTimes(1)
        await act(async () => { resolve?.({ data: null }) })
    })

    it('reports saved but readback unavailable without a complete success message', async () => {
        const dialog = await editMethod()
        api.get.mockRejectedValue(new Error('unavailable'))
        fireEvent.click(dialog.getByRole('button', { name: 'Update' }))
        expect(await screen.findByText(/saved.*reload/i)).toBeVisible()
        expect(screen.queryByText('Updated successfully')).not.toBeInTheDocument()
    })

    it('reads the warehouse identity and country scope without discarding or rewriting either', () => {
        expect(admitAdminShippingMethods([method])[0]).toMatchObject({
            fulfillmentWarehouseId: warehouseId, availableCountries: method.availableCountries,
        })
        expect(admitAdminShippingMethods([{ ...method, fulfillmentWarehouseId: null }])[0].fulfillmentWarehouseId).toBeNull()
        expect(() => admitAdminShippingMethods([{ ...method, fulfillmentWarehouseId: 'not-a-warehouse-id' }])).toThrow()
    })

    it('preserves restricted countries and the warehouse on an unrelated rate-only save and reads them back', async () => {
        const dialog = await editMethod()
        expect(screen.getByText('Egypt (EG), Saudi Arabia (SA)')).toBeVisible()
        expect(dialog.getByRole('combobox', { name: 'Country scope' })).toHaveValue('selected')
        expect(dialog.getByRole('listbox', { name: 'Select shipping countries' })).toHaveValue(['EG', 'SA'])
        expect(dialog.getByRole('combobox', { name: 'Fulfillment Warehouse' })).toHaveValue(warehouseId)
        fireEvent.change(dialog.getByRole('spinbutton', { name: 'Base Rate' }), { target: { value: '40' } })
        fireEvent.click(dialog.getByRole('button', { name: 'Update' }))
        await waitFor(() => expect(api.put).toHaveBeenCalledWith(`/shippingmethods/${method.id}`, expect.objectContaining({
            baseRate: 40, availableCountries: method.availableCountries, fulfillmentWarehouseId: warehouseId,
        })))
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
        expect(await screen.findByText('40 EGP')).toBeVisible()
        expect(screen.getByText('Egypt (EG), Saudi Arabia (SA)')).toBeVisible()
        expect(screen.getByText('Synthetic warehouse (TEST)')).toBeVisible()
        expect(api.post).not.toHaveBeenCalled()
    })

    it('creates a selected-country method with shared rates, and reads the saved scope back', async () => {
        savedMethods = []
        setup()
        await waitFor(() => expect(api.get).toHaveBeenCalledTimes(2))
        fireEvent.click(screen.getByRole('button', { name: /Add Shipping Method/ }))
        const dialog = within(screen.getByRole('dialog'))
        fireEvent.change(dialog.getByRole('textbox', { name: 'Name (English)' }), { target: { value: 'Synthetic country rates' } })
        fireEvent.change(dialog.getByRole('spinbutton', { name: 'Base Rate' }), { target: { value: '35' } })
        fireEvent.change(dialog.getByRole('combobox', { name: 'Country scope' }), { target: { value: 'selected' } })
        await userEvent.selectOptions(dialog.getByRole('listbox', { name: 'Select shipping countries' }), ['AE', 'EG'])
        fireEvent.click(dialog.getByRole('button', { name: 'Save' }))
        await waitFor(() => expect(api.post).toHaveBeenCalledWith('/shippingmethods', expect.objectContaining({
            name: 'Synthetic country rates', baseRate: 35, availableCountries: '["AE","EG"]', fulfillmentWarehouseId: warehouseId,
        })))
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
        expect(await screen.findByText('United Arab Emirates (AE), Egypt (EG)')).toBeVisible()
        expect(api.put).not.toHaveBeenCalled()
    })

    it('blocks an empty selected scope instead of silently saving all, then permits an explicit all-countries choice', async () => {
        const dialog = await editMethod()
        await userEvent.deselectOptions(dialog.getByRole('listbox', { name: 'Select shipping countries' }), ['EG', 'SA'])
        fireEvent.click(dialog.getByRole('button', { name: 'Update' }))
        expect(dialog.getByRole('alert')).toHaveTextContent('Select at least one valid shipping country')
        expect(api.put).not.toHaveBeenCalled()
        fireEvent.change(dialog.getByRole('combobox', { name: 'Country scope' }), { target: { value: 'all' } })
        fireEvent.click(dialog.getByRole('button', { name: 'Update' }))
        await waitFor(() => expect(api.put).toHaveBeenCalledWith(`/shippingmethods/${method.id}`, expect.objectContaining({
            availableCountries: null, fulfillmentWarehouseId: warehouseId,
        })))
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
        expect(await screen.findByText('All countries')).toBeVisible()
    })

    it('updates the selected country set without replacing the shared rate or warehouse', async () => {
        const dialog = await editMethod()
        const countries = dialog.getByRole('listbox', { name: 'Select shipping countries' })
        await userEvent.deselectOptions(countries, ['SA'])
        await userEvent.selectOptions(countries, ['AE'])
        fireEvent.click(dialog.getByRole('button', { name: 'Update' }))
        await waitFor(() => expect(api.put).toHaveBeenCalledWith(`/shippingmethods/${method.id}`, expect.objectContaining({
            availableCountries: '["AE","EG"]', baseRate: 25, perKgRate: 2, freeShippingThreshold: 500, fulfillmentWarehouseId: warehouseId,
        })))
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
        expect(await screen.findByText('United Arab Emirates (AE), Egypt (EG)')).toBeVisible()
    })

    it.each([null, undefined])('keeps existing unrestricted scope %s unrestricted on a rate save', async (availableCountries) => {
        savedMethods = [{ ...method, availableCountries }]
        const dialog = await editMethod()
        expect(dialog.getByRole('combobox', { name: 'Country scope' })).toHaveValue('all')
        expect(dialog.queryByRole('listbox')).not.toBeInTheDocument()
        fireEvent.click(dialog.getByRole('button', { name: 'Update' }))
        await waitFor(() => expect(api.put).toHaveBeenCalledWith(`/shippingmethods/${method.id}`, expect.objectContaining({ availableCountries: null })))
    })

    it.each(['["ZZ"]', '[]', 'not-json', 'null', '["EG",null]'])('fails closed on malformed persisted country scope %s', async (availableCountries) => {
        savedMethods = [{ ...method, availableCountries }]
        expect(() => admitAdminShippingMethods(savedMethods)).toThrow()
        setup()
        expect(await screen.findByRole('alert')).toHaveTextContent('Saved shipping country scope is invalid')
        expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
        expect(screen.queryByText('All countries')).not.toBeInTheDocument()
        expect(api.put).not.toHaveBeenCalled()
        expect(api.post).not.toHaveBeenCalled()
    })

    it('localizes country authoring, shared-rate guidance, readback and validation in Arabic', async () => {
        await i18n.changeLanguage('ar')
        setup()
        expect(await screen.findByText('مصر (EG), المملكة العربية السعودية (SA)')).toBeVisible()
        fireEvent.click(screen.getByRole('button', { name: i18n.t('common.edit') }))
        const dialog = within(screen.getByRole('dialog'))
        expect(dialog.getByRole('combobox', { name: 'نطاق الدول' })).toHaveValue('selected')
        expect(dialog.getByText(/تُطبق هذه الأسعار على جميع الدول المختارة/)).toBeVisible()
        expect(dialog.getByRole('option', { name: 'مصر (EG)' })).toBeInTheDocument()
        await act(async () => {
            fireEvent.change(dialog.getByRole('combobox', { name: 'نطاق الدول' }), { target: { value: 'all' } })
            fireEvent.change(dialog.getByRole('combobox', { name: 'نطاق الدول' }), { target: { value: 'selected' } })
        })
        fireEvent.click(dialog.getByRole('button', { name: i18n.t('common.update') }))
        expect(dialog.getByRole('alert')).toHaveTextContent('اختر دولة شحن صالحة واحدة على الأقل')
        expect(api.put).not.toHaveBeenCalled()
    })
})

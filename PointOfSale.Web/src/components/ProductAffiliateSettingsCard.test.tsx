import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import ProductAffiliateSettingsCard from './ProductAffiliateSettingsCard'
import { ConfirmDialogProvider } from '../common/confirm/ConfirmDialogProvider'

const getSettingsMock = vi.hoisted(() => vi.fn())
const getCategoryTreeMock = vi.hoisted(() => vi.fn())
const enableWithCategoryMock = vi.hoisted(() => vi.fn())
const disableMock = vi.hoisted(() => vi.fn())
const readinessRefetchMock = vi.hoisted(() => vi.fn())
const readinessState = vi.hoisted(() => ({
    loading: false,
    isReady: true,
    storefrontLive: true,
    hasPaymentMethod: true,
    hasShippingMethod: true,
    missingRequirements: [] as string[],
}))
const useAuthStoreMock = vi.hoisted(() => vi.fn((selector: (state: unknown) => unknown) => selector({
    user: { role: 'Admin' },
})))

vi.mock('../services/adminProductAffiliateApi', () => ({
    default: {
        getSettings: getSettingsMock,
        getCategoryTree: getCategoryTreeMock,
        enableWithCategory: enableWithCategoryMock,
        disable: disableMock,
    },
}))

vi.mock('../hooks/useAffiliateReadiness', () => ({
    useAffiliateReadiness: () => ({
        ...readinessState,
        refetch: readinessRefetchMock,
    }),
}))

vi.mock('../store/authStore', () => ({
    useAuthStore: useAuthStoreMock,
}))

const translate = vi.hoisted(() => {
    const labels: Record<string, string> = {
        'affiliateSettingsCard.title': 'Affiliate Marketing',
        'affiliateSettingsCard.subtitle': 'Enable commission-based referrals for this product',
        'affiliateSettingsCard.loading': 'Loading affiliate settings',
        'affiliateSettingsCard.statusEnabled': 'Enabled',
        'affiliateSettingsCard.statusDisabled': 'Disabled',
        'affiliateSettingsCard.categoryLabel': 'Marketplace Category *',
        'affiliateSettingsCard.selectPlaceholder': '— Select a category —',
        'affiliateSettingsCard.categoriesUnavailableOption': 'No categories available',
        'affiliateSettingsCard.inactiveSuffix': '(inactive)',
        'affiliateSettingsCard.categoryHelpDefault': 'Choose the marketplace category this product should use for affiliate referrals.',
        'affiliateSettingsCard.categoryHelpInactive': 'Selected category is inactive. Choose a different category or contact the platform owner.',
        'affiliateSettingsCard.categoryHelpMissing': 'Previously assigned category no longer exists. Please select a new category.',
        'affiliateSettingsCard.categoryHelpEmpty': 'No categories available. Contact the platform owner to set up marketplace categories.',
        'affiliateSettingsCard.rateLabel': 'Commission Rate (%) *',
        'affiliateSettingsCard.rateHelp': 'Percentage of paid amount earned by affiliates (0.01% – 50%)',
        'affiliateSettingsCard.maxLabel': 'Max Commission Per Item',
        'affiliateSettingsCard.maxPlaceholder': 'No cap',
        'affiliateSettingsCard.maxHelp': 'Optional cap on commission per item. Leave empty for no limit.',
        'affiliateSettingsCard.infoNotice': 'Commission is calculated at order time.',
        'affiliateSettingsCard.unsavedBanner': 'Unsaved changes. Save to apply them, or use Reset Changes to discard them.',
        'affiliateSettingsCard.resetChanges': 'Reset Changes',
        'affiliateSettingsCard.cancelButton': 'Cancel',
        'affiliateSettingsCard.enableButton': 'Enable Affiliate Marketing',
        'affiliateSettingsCard.enablingLabel': 'Enabling...',
        'affiliateSettingsCard.disableButton': 'Disable Affiliate',
        'affiliateSettingsCard.workingLabel': 'Working...',
        'affiliateSettingsCard.saveButton': 'Save Changes',
        'affiliateSettingsCard.savingLabel': 'Saving...',
        'affiliateSettingsCard.successEnabled': 'Affiliate marketing enabled successfully',
        'affiliateSettingsCard.successSaved': 'Affiliate settings saved successfully',
        'affiliateSettingsCard.successDisabled': 'Affiliate marketing disabled',
        'affiliateSettingsCard.settingsLoadError': 'Unable to load current affiliate settings. Refresh and try again.',
        'affiliateSettingsCard.unsavedDialogTitle': 'Unsaved affiliate changes',
        'affiliateSettingsCard.unsavedDialogDescription': 'You have unsaved affiliate configuration changes. Leave without saving?',
        'affiliateSettingsCard.stayLabel': 'Stay',
        'affiliateSettingsCard.leaveLabel': 'Leave',
        'affiliateSettingsCard.errors.notReady': 'Affiliate system is not ready. Complete the setup requirements first.',
        'affiliateSettingsCard.errors.categoryRequired': 'A marketplace category is required while affiliate marketing is enabled.',
        'affiliateSettingsCard.errors.categoryInactive': 'The selected category is inactive. Please select a different category.',
        'affiliateSettingsCard.errors.rateRequired': 'Commission rate is required.',
        'affiliateSettingsCard.errors.rateInvalid': 'Commission rate must be a plain number.',
        'affiliateSettingsCard.errors.rateRange': 'Commission rate must be between 0.01% and 50%.',
        'affiliateSettingsCard.errors.ratePrecision': 'Commission rate supports at most four decimal places.',
        'affiliateSettingsCard.errors.maxInvalid': 'Max commission per item must be a plain number.',
        'affiliateSettingsCard.errors.maxPositive': 'Max commission per item must be greater than zero when set.',
        'affiliateSettingsCard.errors.maxPrecision': 'Max commission per item supports at most four decimal places.',
        'affiliateSettingsCard.mutationErrors.network': 'Cannot reach the server. Check your connection and try again.',
        'affiliateSettingsCard.mutationErrors.rejected': 'The affiliate settings could not be saved. Review the values and try again.',
        'affiliateSettingsCard.mutationErrors.forbidden': 'You do not have permission to manage affiliate settings.',
        'affiliateSettingsCard.mutationErrors.notFound': 'This product or its affiliate settings no longer exist. Refresh the list and try again.',
        'affiliateSettingsCard.mutationErrors.conflict': 'Conflict occurred. The latest values were reloaded.',
        'affiliateSettingsCard.mutationErrors.unavailable': 'The server is temporarily unavailable. Try again shortly.',
        'affiliateSettingsCard.mutationErrors.server': 'Something went wrong while saving the affiliate settings. Try again.',
    }

    const t = (key: string) => labels[key] || key
    return { t }
})

vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: translate.t, i18n: { resolvedLanguage: 'en', language: 'en' } }),
}))

const categoryTree = [
    {
        id: 'main-category',
        name: 'Electronics',
        slug: 'electronics',
        isActive: true,
        subcategories: [
            { id: 'category-a', name: 'Phones', slug: 'phones', isActive: true },
            { id: 'category-b', name: 'Laptops', slug: 'laptops', isActive: true },
        ],
    },
    {
        id: 'root-only-category',
        name: 'Books & Media',
        slug: 'books-media',
        isActive: true,
        subcategories: [],
    },
]

function renderCard(props = {}) {
    return render(
        <ConfirmDialogProvider>
            <MemoryRouter>
                <ProductAffiliateSettingsCard productId="product-1" {...props} />
            </MemoryRouter>
        </ConfirmDialogProvider>
    )
}

function deferred<T>() {
    let resolve!: (value: T) => void
    let reject!: (reason?: unknown) => void
    const promise = new Promise<T>((res, rej) => {
        resolve = res
        reject = rej
    })
    return { promise, resolve, reject }
}

function enabledBaseline() {
    return {
        productId: 'product-1',
        isEnabled: true,
        commissionRate: 0.1,
        maxCommissionPerItem: 25,
        templateType: 'ClassicV1',
        affiliateCategoryId: 'category-a',
    }
}

describe('product affiliate settings edit authority', () => {
    beforeEach(() => {
        getSettingsMock.mockReset()
        getCategoryTreeMock.mockReset()
        enableWithCategoryMock.mockReset()
        disableMock.mockReset()
        readinessRefetchMock.mockReset()
        Object.assign(readinessState, {
            loading: false,
            isReady: true,
            storefrontLive: true,
            hasPaymentMethod: true,
            hasShippingMethod: true,
            missingRequirements: [],
        })
        getCategoryTreeMock.mockResolvedValue(categoryTree)
    })

    it('preserves unsaved edits during lifecycle refetch and resets to backend authority', async () => {
        getSettingsMock
            .mockResolvedValueOnce(enabledBaseline())
            .mockResolvedValueOnce({
                ...enabledBaseline(),
                commissionRate: 0.2,
                maxCommissionPerItem: 40,
                affiliateCategoryId: 'category-b',
            })

        renderCard()

        await screen.findByText('Affiliate Marketing')
        const rateInput = document.querySelector<HTMLInputElement>('#affiliate-commission-rate')
        const maxCommissionInput = document.querySelector<HTMLInputElement>('#affiliate-max-commission')
        const categorySelect = document.querySelector<HTMLSelectElement>('#affiliate-category-select')

        expect(rateInput).not.toBeNull()
        expect(maxCommissionInput).not.toBeNull()
        expect(categorySelect).not.toBeNull()
        expect(rateInput).toHaveValue(10)

        fireEvent.change(rateInput!, { target: { value: '12.50' } })
        expect(screen.getByText(/Unsaved changes/)).toBeInTheDocument()

        fireEvent(window, new Event('focus'))

        await waitFor(() => expect(getSettingsMock).toHaveBeenCalledTimes(2))
        expect(rateInput).toHaveValue(12.5)

        fireEvent.click(screen.getByRole('button', { name: 'Reset Changes' }))

        await waitFor(() => expect(screen.queryByText(/Unsaved changes/)).not.toBeInTheDocument())
        expect(rateInput).toHaveValue(20)
        expect(maxCommissionInput).toHaveValue(40)
        expect(categorySelect).toHaveValue('category-b')
    })

    it('exposes labels and human-readable state to assistive technology', async () => {
        getSettingsMock.mockResolvedValue(enabledBaseline())

        renderCard()

        const categorySelect = await screen.findByLabelText(/marketplace category/i)
        const rateInput = screen.getByLabelText(/commission rate/i)
        const maxCommissionInput = screen.getByLabelText(/max commission per item/i)

        expect(categorySelect).toHaveAccessibleDescription(/Electronics -> Phones/)
        expect(rateInput).toHaveAccessibleDescription(/Percentage of paid amount earned by affiliates/)
        expect(maxCommissionInput).toHaveAccessibleDescription(/Optional cap on commission per item/)

        fireEvent.change(rateInput, { target: { value: '12.50' } })

        expect(screen.getByRole('status')).toHaveTextContent(/Save to apply them, or use Reset Changes to discard them/)
    })

    it('reports dirty and busy dialog state and provides an explicit cancel action', async () => {
        getSettingsMock.mockResolvedValue(enabledBaseline())
        const onBusyChange = vi.fn()
        const onDirtyChange = vi.fn()
        const onCancel = vi.fn()
        const pendingSave = deferred<ReturnType<typeof enabledBaseline>>()
        enableWithCategoryMock.mockReturnValue(pendingSave.promise)

        renderCard({ onBusyChange, onDirtyChange, onCancel })
        const rateInput = await screen.findByRole('spinbutton', { name: 'Commission Rate (%) *' })

        expect(onBusyChange).toHaveBeenCalledWith(false)
        expect(onDirtyChange).toHaveBeenCalledWith(false)
        fireEvent.change(rateInput, { target: { value: '12' } })
        await waitFor(() => expect(onDirtyChange).toHaveBeenLastCalledWith(true))

        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
        expect(onCancel).toHaveBeenCalledTimes(1)

        fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))
        await waitFor(() => expect(onBusyChange).toHaveBeenLastCalledWith(true))
        expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled()

        pendingSave.resolve({ ...enabledBaseline(), commissionRate: 0.12 })
        await waitFor(() => expect(onBusyChange).toHaveBeenLastCalledWith(false))
    })

    it('does not publish a stale accepted mutation after the dialog unmounts', async () => {
        getSettingsMock.mockResolvedValue(enabledBaseline())
        const pendingSave = deferred<ReturnType<typeof enabledBaseline>>()
        enableWithCategoryMock.mockReturnValue(pendingSave.promise)
        const onSettingsChanged = vi.fn()
        const view = renderCard({ onSettingsChanged })

        fireEvent.change(await screen.findByRole('spinbutton', { name: 'Commission Rate (%) *' }), {
            target: { value: '12' },
        })
        fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))
        await waitFor(() => expect(enableWithCategoryMock).toHaveBeenCalledTimes(1))

        view.unmount()
        pendingSave.resolve({ ...enabledBaseline(), commissionRate: 0.12 })
        await Promise.resolve()

        expect(onSettingsChanged).not.toHaveBeenCalled()
    })

    it('uses persisted policy participation instead of inferred lifecycle eligibility', async () => {
        getSettingsMock.mockResolvedValue({
            productId: 'product-1',
            isEnabled: false,
            isPolicyEnabled: true,
            ...enabledBaseline(),
        })

        renderCard()

        await waitFor(() => expect(screen.getByTestId('affiliate-policy-status')).toHaveTextContent('Enabled'))
        expect(screen.getByTestId('affiliate-policy-status')).not.toHaveTextContent('Disabled')
        expect(screen.getByRole('button', { name: /save changes/i })).toBeInTheDocument()
    })

    it('warns before refresh or tab close while edits are unsaved', async () => {
        getSettingsMock.mockResolvedValue(enabledBaseline())

        renderCard()

        const rateInput = await screen.findByLabelText(/commission rate/i)

        expect(window.dispatchEvent(new Event('beforeunload', { cancelable: true }))).toBe(true)

        fireEvent.change(rateInput, { target: { value: '12.50' } })
        await screen.findByText(/Unsaved changes/)

        const beforeUnload = new Event('beforeunload', { cancelable: true })
        expect(window.dispatchEvent(beforeUnload)).toBe(false)
        expect(beforeUnload.defaultPrevented).toBe(true)

        fireEvent.click(screen.getByRole('button', { name: 'Reset Changes' }))
        await waitFor(() => expect(screen.queryByText(/Unsaved changes/)).not.toBeInTheDocument())

        expect(window.dispatchEvent(new Event('beforeunload', { cancelable: true }))).toBe(true)
    })

    it('asks for confirmation on browser back while edits are unsaved', async () => {
        getSettingsMock.mockResolvedValue(enabledBaseline())
        renderCard()

        const rateInput = await screen.findByLabelText(/commission rate/i)
        fireEvent.change(rateInput, { target: { value: '12.50' } })
        await screen.findByText(/Unsaved changes/)

        fireEvent.popState(window)

        expect(await screen.findByText('Unsaved affiliate changes')).toBeInTheDocument()
        expect(screen.getByText('You have unsaved affiliate configuration changes. Leave without saving?')).toBeInTheDocument()
    })

    it('does not render editable controls when backend settings authority is unavailable', async () => {
        getSettingsMock.mockRejectedValueOnce(new Error('network'))

        renderCard()

        expect(await screen.findByText('Unable to load current affiliate settings. Refresh and try again.')).toBeInTheDocument()
        expect(document.querySelector('#affiliate-commission-rate')).toBeNull()
    })

    it('fails closed when a replayed settings response belongs to another product', async () => {
        getSettingsMock.mockResolvedValue({
            productId: 'product-a',
            isEnabled: true,
            commissionRate: 0.3,
            maxCommissionPerItem: 99,
            templateType: 'ClassicV1',
            affiliateCategoryId: 'category-a',
        })

        renderCard()

        expect(await screen.findByText('Unable to load current affiliate settings. Refresh and try again.')).toBeInTheDocument()
        expect(document.querySelector('#affiliate-commission-rate')).toBeNull()
        expect(screen.queryByTestId('affiliate-policy-status')).not.toBeInTheDocument()
    })

    it('blocks rapid duplicate save dispatch before React disables the button', async () => {
        getSettingsMock.mockResolvedValue(enabledBaseline())
        enableWithCategoryMock.mockImplementation(() => new Promise(() => {}))

        renderCard()

        const saveButton = await screen.findByRole('button', { name: /save changes/i })

        fireEvent.click(saveButton)
        fireEvent.click(saveButton)

        expect(enableWithCategoryMock).toHaveBeenCalledTimes(1)
        expect(enableWithCategoryMock).toHaveBeenCalledWith('product-1', 0.1, 'category-a', 25)
    })

    it('allows selecting a source-defined root category that has no children', async () => {
        getSettingsMock.mockResolvedValue({
            ...enabledBaseline(),
            isEnabled: false,
        })
        enableWithCategoryMock.mockResolvedValue({
            productId: 'product-1',
            isEnabled: true,
            isPolicyEnabled: true,
            commissionRate: 0.1,
            maxCommissionPerItem: 25,
            templateType: 'ClassicV1',
            affiliateCategoryId: 'root-only-category',
        })

        renderCard()

        const categorySelect = await screen.findByLabelText(/marketplace category/i)
        fireEvent.change(categorySelect, { target: { value: 'root-only-category' } })
        fireEvent.click(screen.getByRole('button', { name: /enable affiliate marketing/i }))

        await waitFor(() => expect(enableWithCategoryMock).toHaveBeenCalledWith(
            'product-1',
            0.1,
            'root-only-category',
            25,
        ))
    })

    it('keeps displayed eligibility status bound to the latest backend baseline while edits are dirty', async () => {
        getSettingsMock
            .mockResolvedValueOnce(enabledBaseline())
            .mockResolvedValueOnce({
                productId: 'product-1',
                isEnabled: false,
                isPolicyEnabled: false,
                commissionRate: 0.2,
                maxCommissionPerItem: 40,
                templateType: 'ClassicV1',
                affiliateCategoryId: 'category-b',
            })

        renderCard()

        const rateInput = await screen.findByLabelText(/commission rate/i)
        expect(screen.getByTestId('affiliate-policy-status')).toHaveTextContent('Enabled')

        fireEvent.change(rateInput, { target: { value: '12.50' } })
        fireEvent(window, new Event('focus'))

        await waitFor(() => expect(getSettingsMock).toHaveBeenCalledTimes(2))

        expect(rateInput).toHaveValue(12.5)
        expect(screen.getByTestId('affiliate-policy-status')).toHaveTextContent('Disabled')
        expect(screen.getByTestId('affiliate-policy-status')).not.toHaveTextContent('Enabled')
        expect(screen.getByRole('button', { name: /enable affiliate marketing/i })).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: /save changes/i })).not.toBeInTheDocument()
    })

    it('does not let stale settings reads overwrite an accepted mutation commitment', async () => {
        const staleRead = deferred<{
            productId: string
            isEnabled: boolean
            commissionRate: number
            maxCommissionPerItem: number | null
            templateType: string
            affiliateCategoryId: string
        }>()

        getSettingsMock
            .mockResolvedValueOnce({
                ...enabledBaseline(),
                isEnabled: false,
            })
            .mockReturnValueOnce(staleRead.promise)

        enableWithCategoryMock.mockResolvedValue({
            productId: 'product-1',
            isEnabled: true,
            isPolicyEnabled: true,
            commissionRate: 0.2,
            maxCommissionPerItem: 40,
            templateType: 'ClassicV1',
            affiliateCategoryId: 'category-b',
        })

        renderCard()

        await waitFor(() => expect(screen.getByTestId('affiliate-policy-status')).toHaveTextContent('Disabled'))
        fireEvent(window, new Event('focus'))
        await waitFor(() => expect(getSettingsMock).toHaveBeenCalledTimes(2))

        fireEvent.click(screen.getByRole('button', { name: /enable affiliate marketing/i }))

        await screen.findByText(/Affiliate marketing enabled successfully/)
        expect(screen.getByTestId('affiliate-policy-status')).toHaveTextContent('Enabled')

        staleRead.resolve({
            productId: 'product-1',
            isEnabled: false,
            isPolicyEnabled: false,
            commissionRate: 0.1,
            maxCommissionPerItem: 25,
            templateType: 'ClassicV1',
            affiliateCategoryId: 'category-a',
        })

        await waitFor(() => expect(screen.getByTestId('affiliate-policy-status')).toHaveTextContent('Enabled'))
        expect(screen.getByTestId('affiliate-policy-status')).not.toHaveTextContent('Disabled')
        expect(screen.getByRole('button', { name: /save changes/i })).toBeInTheDocument()
    })

    it('keeps an accepted mutation successful when the host refresh callback fails', async () => {
        // Initial read shows a disabled policy; canonical post-mutation reads
        // reflect the accepted enablement.
        getSettingsMock
            .mockResolvedValueOnce({
                ...enabledBaseline(),
                isEnabled: false,
            })
            .mockResolvedValue(enabledBaseline())
        enableWithCategoryMock.mockResolvedValue({
            productId: 'product-1',
            isEnabled: true,
            isPolicyEnabled: true,
            commissionRate: 0.1,
            maxCommissionPerItem: 25,
            templateType: 'ClassicV1',
            affiliateCategoryId: 'category-a',
        })

        renderCard({ onSettingsChanged: () => { throw new Error('host refresh failed') } })

        await waitFor(() => expect(screen.getByTestId('affiliate-policy-status')).toHaveTextContent('Disabled'))
        fireEvent.click(screen.getByRole('button', { name: /enable affiliate marketing/i }))

        expect(await screen.findByText(/Affiliate marketing enabled successfully/)).toBeInTheDocument()
        expect(screen.getByTestId('affiliate-policy-status')).toHaveTextContent('Enabled')
        expect(screen.queryByText(/Failed to enable affiliate/)).not.toBeInTheDocument()
    })
})

describe('affiliate settings client-side validation', () => {
    beforeEach(() => {
        getSettingsMock.mockReset()
        getCategoryTreeMock.mockReset()
        enableWithCategoryMock.mockReset()
        disableMock.mockReset()
        readinessRefetchMock.mockReset()
        getCategoryTreeMock.mockResolvedValue(categoryTree)
        getSettingsMock.mockResolvedValue(enabledBaseline())
    })

    it('rejects blank, out-of-range, and excess-precision commission rates without dispatching a mutation', async () => {
        renderCard()

        const rateInput = await screen.findByLabelText(/commission rate/i)
        const saveButton = await screen.findByRole('button', { name: /save changes/i })

        fireEvent.change(rateInput, { target: { value: '' } })
        fireEvent.click(saveButton)
        expect(await screen.findByRole('alert')).toHaveTextContent('Commission rate is required.')

        fireEvent.change(rateInput, { target: { value: '75' } })
        fireEvent.click(saveButton)
        expect(await screen.findByRole('alert')).toHaveTextContent('Commission rate must be between 0.01% and 50%.')
        expect(rateInput).toHaveAttribute('aria-invalid', 'true')

        fireEvent.change(rateInput, { target: { value: '10.12345' } })
        fireEvent.click(saveButton)
        expect(await screen.findByRole('alert')).toHaveTextContent('Commission rate supports at most four decimal places.')

        expect(enableWithCategoryMock).not.toHaveBeenCalled()
    })

    it('rejects negative, zero, malformed, overflowing, and excess-precision max caps without partial parseFloat coercion', async () => {
        renderCard()

        const maxInput = await screen.findByLabelText(/max commission per item/i)
        const saveButton = await screen.findByRole('button', { name: /save changes/i })

        fireEvent.change(maxInput, { target: { value: '-5' } })
        fireEvent.click(saveButton)
        expect(await screen.findByRole('alert')).toHaveTextContent('Max commission per item must be a plain number.')
        expect(maxInput).toHaveAttribute('aria-invalid', 'true')

        fireEvent.change(maxInput, { target: { value: '0' } })
        fireEvent.click(saveButton)
        expect(await screen.findByRole('alert')).toHaveTextContent('Max commission per item must be greater than zero when set.')

        fireEvent.change(maxInput, { target: { value: '1.00001' } })
        fireEvent.click(saveButton)
        expect(await screen.findByRole('alert')).toHaveTextContent('Max commission per item supports at most four decimal places.')

        fireEvent.change(maxInput, { target: { value: '12345678901234567' } })
        fireEvent.click(saveButton)
        expect(await screen.findByRole('alert')).toHaveTextContent('Max commission per item supports at most four decimal places.')

        expect(enableWithCategoryMock).not.toHaveBeenCalled()
    })

    it('saves a cleared max cap as null and never coerces empty values to zero', async () => {
        enableWithCategoryMock.mockResolvedValue({
            ...enabledBaseline(),
            maxCommissionPerItem: null,
        })

        renderCard()

        const maxInput = await screen.findByLabelText(/max commission per item/i)
        const saveButton = await screen.findByRole('button', { name: /save changes/i })

        fireEvent.change(maxInput, { target: { value: '' } })
        fireEvent.click(saveButton)

        await waitFor(() => expect(enableWithCategoryMock).toHaveBeenCalledWith(
            'product-1', 0.1, 'category-a', null,
        ))
        expect(await screen.findByText(/Affiliate settings saved successfully/)).toBeInTheDocument()
    })

    it('preserves the backend commission precision when displaying and resubmitting an unchanged rate', async () => {
        getSettingsMock.mockResolvedValue({
            ...enabledBaseline(),
            commissionRate: 0.123456,
        })
        enableWithCategoryMock.mockResolvedValue({
            ...enabledBaseline(),
            commissionRate: 0.123456,
        })

        renderCard()

        expect(await screen.findByLabelText(/commission rate/i)).toHaveValue(12.3456)
        fireEvent.click(screen.getByRole('button', { name: /save changes/i }))

        await waitFor(() => expect(enableWithCategoryMock).toHaveBeenCalledWith(
            'product-1', 0.123456, 'category-a', 25,
        ))
    })

    it('retains untouched fields when saving a commission-only change', async () => {
        enableWithCategoryMock.mockResolvedValue(enabledBaseline())

        renderCard()

        const rateInput = await screen.findByLabelText(/commission rate/i)
        const saveButton = await screen.findByRole('button', { name: /save changes/i })

        fireEvent.change(rateInput, { target: { value: '20' } })
        fireEvent.click(saveButton)

        await waitFor(() => expect(enableWithCategoryMock).toHaveBeenCalledWith(
            'product-1', 0.2, 'category-a', 25,
        ))
        await waitFor(() => expect(
            document.querySelector<HTMLSelectElement>('#affiliate-category-select')!.value,
        ).toBe('category-a'))
        expect(document.querySelector<HTMLInputElement>('#affiliate-max-commission')!.value).toBe('25')
    })

    it('maps server failures to localized safe wording and never renders backend exception text', async () => {
        enableWithCategoryMock.mockRejectedValue({
            response: {
                status: 400,
                data: { message: 'Cannot enable affiliate marketing without assigning a category (policies_product_id_fkey)' },
            },
        })

        renderCard()

        fireEvent.click(await screen.findByRole('button', { name: /save changes/i }))

        expect(await screen.findByRole('alert')).toHaveTextContent(
            'The affiliate settings could not be saved. Review the values and try again.',
        )
        await waitFor(() => expect(getSettingsMock.mock.calls.length).toBeGreaterThanOrEqual(2))
        expect(screen.getByRole('alert')).toHaveTextContent(
            'The affiliate settings could not be saved. Review the values and try again.',
        )
        expect(screen.queryByText(/fkey/i)).not.toBeInTheDocument()
        expect(screen.getByLabelText(/marketplace category/i)).not.toHaveAttribute('aria-invalid', 'true')
    })

    it('maps network failures to localized safe wording', async () => {
        enableWithCategoryMock.mockRejectedValue(new Error('getaddrinfo ENOTFOUND'))

        renderCard()

        fireEvent.click(await screen.findByRole('button', { name: /save changes/i }))

        expect(await screen.findByRole('alert')).toHaveTextContent(
            'Cannot reach the server. Check your connection and try again.',
        )
        expect(screen.queryByText(/ENOTFOUND/)).not.toBeInTheDocument()
    })

    it('keeps the accepted disable result even when canonical reconciliation fails', async () => {
        disableMock.mockResolvedValue({
            ...enabledBaseline(),
            isEnabled: false,
            isPolicyEnabled: false,
        })
        // First call = initial mount read; later calls fail (network flap).
        getSettingsMock.mockResolvedValueOnce(enabledBaseline())
        getSettingsMock.mockRejectedValue(new Error('offline'))

        renderCard()

        fireEvent.click(await screen.findByRole('button', { name: /disable affiliate/i }))

        expect(await screen.findByText(/Affiliate marketing disabled/)).toBeInTheDocument()
        expect(disableMock).toHaveBeenCalledWith('product-1')
        await waitFor(() => expect(getSettingsMock.mock.calls.length).toBeGreaterThanOrEqual(2))
        expect(screen.getByTestId('affiliate-policy-status')).toHaveTextContent('Disabled')
    })

    it('shows explicit Enabled and Disabled participation wording instead of ambiguous checkboxes', async () => {
        renderCard()

        await screen.findByText('Affiliate Marketing')
        expect(screen.getByTestId('affiliate-policy-status')).toHaveTextContent('Enabled')
        expect(screen.getByRole('button', { name: /disable affiliate/i })).toBeInTheDocument()
        expect(document.querySelectorAll('input[type="checkbox"]').length).toBe(0)
    })

    it('keeps save and disable available for an enabled policy when tenant readiness degrades', async () => {
        readinessState.isReady = false
        readinessState.storefrontLive = false
        readinessState.missingRequirements = ['storefront']
        enableWithCategoryMock.mockResolvedValue(enabledBaseline())
        disableMock.mockResolvedValue({
            ...enabledBaseline(),
            isEnabled: false,
            isPolicyEnabled: false,
        })

        renderCard()

        fireEvent.change(await screen.findByLabelText(/commission rate/i), { target: { value: '12' } })
        fireEvent.click(screen.getByRole('button', { name: /save changes/i }))
        await waitFor(() => expect(enableWithCategoryMock).toHaveBeenCalledWith(
            'product-1', 0.12, 'category-a', 25,
        ))

        fireEvent.click(screen.getByRole('button', { name: /disable affiliate/i }))
        await waitFor(() => expect(disableMock).toHaveBeenCalledWith('product-1'))
    })
})

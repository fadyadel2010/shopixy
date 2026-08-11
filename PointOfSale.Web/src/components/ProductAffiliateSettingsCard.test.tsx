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
        loading: false,
        isReady: true,
        storefrontLive: true,
        hasPaymentMethod: true,
        hasShippingMethod: true,
        missingRequirements: [],
        refetch: readinessRefetchMock,
    }),
}))

vi.mock('../store/authStore', () => ({
    useAuthStore: useAuthStoreMock,
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
]

function renderCard() {
    return render(
        <ConfirmDialogProvider>
            <MemoryRouter>
                <ProductAffiliateSettingsCard productId="product-1" />
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

describe('product affiliate settings edit authority', () => {
    beforeEach(() => {
        getSettingsMock.mockReset()
        getCategoryTreeMock.mockReset()
        enableWithCategoryMock.mockReset()
        disableMock.mockReset()
        readinessRefetchMock.mockReset()
        getCategoryTreeMock.mockResolvedValue(categoryTree)
    })

    it('preserves unsaved edits during lifecycle refetch and resets to backend authority', async () => {
        getSettingsMock
            .mockResolvedValueOnce({
                productId: 'product-1',
                isEnabled: true,
                commissionRate: 0.1,
                maxCommissionPerItem: 25,
                templateType: 'ClassicV1',
                affiliateCategoryId: 'category-a',
            })
            .mockResolvedValueOnce({
                productId: 'product-1',
                isEnabled: true,
                commissionRate: 0.2,
                maxCommissionPerItem: 40,
                templateType: 'ClassicV1',
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

        fireEvent.change(rateInput, { target: { value: '12.50' } })
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
        getSettingsMock.mockResolvedValue({
            productId: 'product-1',
            isEnabled: true,
            commissionRate: 0.1,
            maxCommissionPerItem: 25,
            templateType: 'ClassicV1',
            affiliateCategoryId: 'category-a',
        })

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

    it('warns before refresh or tab close while edits are unsaved', async () => {
        getSettingsMock.mockResolvedValue({
            productId: 'product-1',
            isEnabled: true,
            commissionRate: 0.1,
            maxCommissionPerItem: 25,
            templateType: 'ClassicV1',
            affiliateCategoryId: 'category-a',
        })

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
        getSettingsMock.mockResolvedValue({
            productId: 'product-1',
            isEnabled: true,
            commissionRate: 0.1,
            maxCommissionPerItem: 25,
            templateType: 'ClassicV1',
            affiliateCategoryId: 'category-a',
        })
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
        expect(screen.queryByText('Current: Active')).not.toBeInTheDocument()
        expect(screen.queryByText('Current: Inactive')).not.toBeInTheDocument()
    })

    it('blocks rapid duplicate save dispatch before React disables the button', async () => {
        getSettingsMock.mockResolvedValue({
            productId: 'product-1',
            isEnabled: true,
            commissionRate: 0.1,
            maxCommissionPerItem: 25,
            templateType: 'ClassicV1',
            affiliateCategoryId: 'category-a',
        })
        enableWithCategoryMock.mockImplementation(() => new Promise(() => {}))

        renderCard()

        const saveButton = await screen.findByRole('button', { name: /save changes/i })

        fireEvent.click(saveButton)
        fireEvent.click(saveButton)

        expect(enableWithCategoryMock).toHaveBeenCalledTimes(1)
        expect(enableWithCategoryMock).toHaveBeenCalledWith('product-1', 0.1, 'category-a', 25)
    })

    it('keeps displayed eligibility status bound to the latest backend baseline while edits are dirty', async () => {
        getSettingsMock
            .mockResolvedValueOnce({
                productId: 'product-1',
                isEnabled: true,
                commissionRate: 0.1,
                maxCommissionPerItem: 25,
                templateType: 'ClassicV1',
                affiliateCategoryId: 'category-a',
            })
            .mockResolvedValueOnce({
                productId: 'product-1',
                isEnabled: false,
                commissionRate: 0.2,
                maxCommissionPerItem: 40,
                templateType: 'ClassicV1',
                affiliateCategoryId: 'category-b',
            })

        renderCard()

        const rateInput = await screen.findByLabelText(/commission rate/i)
        expect(screen.getByText('Current: Active')).toBeInTheDocument()

        fireEvent.change(rateInput, { target: { value: '12.50' } })
        fireEvent(window, new Event('focus'))

        await waitFor(() => expect(getSettingsMock).toHaveBeenCalledTimes(2))

        expect(rateInput).toHaveValue(12.5)
        expect(screen.getByText('Current: Inactive')).toBeInTheDocument()
        expect(screen.queryByText('Current: Active')).not.toBeInTheDocument()
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
                productId: 'product-1',
                isEnabled: false,
                commissionRate: 0.1,
                maxCommissionPerItem: 25,
                templateType: 'ClassicV1',
                affiliateCategoryId: 'category-a',
            })
            .mockReturnValueOnce(staleRead.promise)

        enableWithCategoryMock.mockResolvedValue({
            productId: 'product-1',
            isEnabled: true,
            commissionRate: 0.2,
            maxCommissionPerItem: 40,
            templateType: 'ClassicV1',
            affiliateCategoryId: 'category-b',
        })

        renderCard()

        await screen.findByText('Current: Inactive')
        fireEvent(window, new Event('focus'))
        await waitFor(() => expect(getSettingsMock).toHaveBeenCalledTimes(2))

        fireEvent.click(screen.getByRole('button', { name: /enable affiliate marketing/i }))

        await screen.findByText(/Affiliate marketing enabled successfully/)
        expect(screen.getByText('Current: Active')).toBeInTheDocument()

        staleRead.resolve({
            productId: 'product-1',
            isEnabled: false,
            commissionRate: 0.1,
            maxCommissionPerItem: 25,
            templateType: 'ClassicV1',
            affiliateCategoryId: 'category-a',
        })

        await waitFor(() => expect(screen.getByText('Current: Active')).toBeInTheDocument())
        expect(screen.queryByText('Current: Inactive')).not.toBeInTheDocument()
        expect(screen.getByRole('button', { name: /save changes/i })).toBeInTheDocument()
    })
})

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const getStoreConfigMock = vi.hoisted(() => vi.fn())
const updateStoreConfigMock = vi.hoisted(() => vi.fn())
const updateStorefrontStatusMock = vi.hoisted(() => vi.fn())
const showErrorMock = vi.hoisted(() => vi.fn())
const showSuccessMock = vi.hoisted(() => vi.fn())
const showInfoMock = vi.hoisted(() => vi.fn())
const tMock = vi.hoisted(() => vi.fn((_key: string, fallback?: string) => fallback))
const currentUser = vi.hoisted(() => ({
    id: '11111111-1111-4111-8111-111111111111',
    tenantId: '22222222-2222-4222-8222-222222222222',
    role: 'Admin',
    navigationCapabilities: ['commercehub.storefront.manage'],
    vendorLifecycleAuthority: {
        vendorId: '22222222-2222-4222-8222-222222222222',
        status: 'Active',
        debtStatus: 'Clear',
        tenantActive: true,
        marketplaceApproved: true,
        kycVerified: true,
        isDebtBlocked: false,
        blocksCatalog: false,
        blocksOrders: false,
        blocksStorefront: false,
        blocksAnalytics: false,
        blocksPayments: false,
        blocksPayouts: false,
        blocksLogin: false,
    },
}))

function resetCurrentUser() {
    Object.assign(currentUser, {
        id: '11111111-1111-4111-8111-111111111111',
        tenantId: '22222222-2222-4222-8222-222222222222',
        role: 'Admin',
        navigationCapabilities: ['commercehub.storefront.manage'],
        vendorLifecycleAuthority: {
            vendorId: '22222222-2222-4222-8222-222222222222',
            status: 'Active',
            debtStatus: 'Clear',
            tenantActive: true,
            marketplaceApproved: true,
            kycVerified: true,
            isDebtBlocked: false,
            blocksCatalog: false,
            blocksOrders: false,
            blocksStorefront: false,
            blocksAnalytics: false,
            blocksPayments: false,
            blocksPayouts: false,
            blocksLogin: false,
        },
    })
}

vi.mock('../modules/storefront/services', () => ({
    storeConfigService: {
        getStoreConfig: getStoreConfigMock,
        updateStoreConfig: updateStoreConfigMock,
        updateStorefrontStatus: updateStorefrontStatusMock,
    },
}))

vi.mock('../modules/storefront/services/index.js', () => ({
    storeConfigService: {
        getStoreConfig: getStoreConfigMock,
        updateStoreConfig: updateStoreConfigMock,
        updateStorefrontStatus: updateStorefrontStatusMock,
    },
}))

vi.mock('../components/Toast', () => ({
    useToast: () => ({
        showError: showErrorMock,
        showSuccess: showSuccessMock,
        showInfo: showInfoMock,
    }),
}))

vi.mock('../store/authStore', () => ({
    useAuthStore: () => ({
        user: currentUser,
    }),
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        i18n: { language: 'en' },
        t: tMock,
    }),
}))

const backendConfig = (overrides = {}) => ({
    tenantId: '22222222-2222-4222-8222-222222222222',
    rowVersion: 'rv-1',
    storeName: 'Backend Store',
    storefrontStatus: 'live',
    maintenanceMessage: '',
    themeId: 'modern',
    updatedAtUtc: '2026-06-27T00:00:00Z',
    ...overrides,
})

async function renderSettings() {
    const { default: StorefrontSettings } = await import('./StorefrontSettings')
    let rendered: ReturnType<typeof render> | undefined
    await act(async () => {
        rendered = render(<StorefrontSettings />)
    })
    return rendered!
}

describe('StorefrontSettings candidate authority', () => {
    beforeEach(() => {
        resetCurrentUser()
        getStoreConfigMock.mockReset()
        updateStoreConfigMock.mockReset()
        updateStorefrontStatusMock.mockReset()
        showErrorMock.mockReset()
        showSuccessMock.mockReset()
        showInfoMock.mockReset()
        tMock.mockClear()
    })

    it('keeps unsaved status edits candidate-only until backend acceptance', async () => {
        getStoreConfigMock.mockResolvedValue(backendConfig())

        await renderSettings()

        expect(await screen.findByText('Backend Store')).toBeInTheDocument()
        expect(screen.getByText('Current Status').nextElementSibling).toHaveTextContent('Live')

        fireEvent.click(screen.getByRole('radio', { name: /Maintenance/ }))

        expect(screen.getByText('Unsaved storefront settings are candidate-only until the backend accepts them.')).toBeInTheDocument()
        expect(screen.getByRole('radio', { name: /Maintenance/ })).toBeChecked()
        expect(screen.getByText('Current Status').nextElementSibling).toHaveTextContent('Live')
    })

    it('renders storefront status management for pending Owner tenants without CommerceHub storefront navigation capability', async () => {
        Object.assign(currentUser, {
            role: 'Owner',
            navigationCapabilities: [],
            vendorLifecycleAuthority: {
                ...currentUser.vendorLifecycleAuthority,
                status: 'Pending',
                marketplaceApproved: false,
                kycVerified: false,
                blocksCatalog: true,
                blocksOrders: true,
                blocksStorefront: true,
                blocksAnalytics: true,
                blocksPayouts: true,
            },
        })
        getStoreConfigMock.mockResolvedValue(backendConfig({ storefrontStatus: 'disabled' }))

        await renderSettings()

        expect(await screen.findByText('Backend Store')).toBeInTheDocument()
        expect(screen.getByText('Store Status')).toBeInTheDocument()
        expect(screen.getByRole('radio', { name: /Disabled/ })).toBeChecked()
        expect(getStoreConfigMock).toHaveBeenCalledTimes(1)
    })

    it('resets candidate edits to the accepted backend baseline', async () => {
        getStoreConfigMock.mockResolvedValue(backendConfig())

        await renderSettings()

        await screen.findByText('Backend Store')

        fireEvent.click(screen.getByRole('radio', { name: /Maintenance/ }))
        expect(screen.getByRole('radio', { name: /Maintenance/ })).toBeChecked()

        fireEvent.click(screen.getByRole('button', { name: 'Reset Changes' }))

        expect(screen.getByRole('radio', { name: /Live/ })).toBeChecked()
        expect(screen.queryByText('Unsaved storefront settings are candidate-only until the backend accepts them.')).not.toBeInTheDocument()
    })

    it('does not preserve candidate edits across remount refresh recovery', async () => {
        getStoreConfigMock
            .mockResolvedValueOnce(backendConfig())
            .mockResolvedValueOnce(backendConfig({ storefrontStatus: 'disabled', rowVersion: 'rv-2' }))

        const rendered = await renderSettings()

        await screen.findByText('Backend Store')
        fireEvent.click(screen.getByRole('radio', { name: /Maintenance/ }))
        expect(screen.getByRole('radio', { name: /Maintenance/ })).toBeChecked()

        rendered.unmount()
        await renderSettings()

        await waitFor(() => expect(getStoreConfigMock).toHaveBeenCalledTimes(2))
        expect(await screen.findByRole('radio', { name: /Disabled/ })).toBeChecked()
        expect(screen.queryByText('Unsaved storefront settings are candidate-only until the backend accepts them.')).not.toBeInTheDocument()
    })

    it('submits candidate intent with backend rowVersion evidence and replaces baseline from accepted response', async () => {
        getStoreConfigMock.mockResolvedValue(backendConfig())
        updateStorefrontStatusMock.mockResolvedValue(backendConfig({
            storefrontStatus: 'maintenance',
            maintenanceMessage: 'Accepted by backend',
            rowVersion: 'rv-2',
            updatedAtUtc: '2026-06-27T01:00:00Z',
        }))

        await renderSettings()

        await screen.findByText('Backend Store')
        fireEvent.click(screen.getByRole('radio', { name: /Maintenance/ }))
        fireEvent.change(screen.getByPlaceholderText("We're making some improvements. Please check back soon!"), {
            target: { value: 'Accepted by backend' },
        })

        fireEvent.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(updateStorefrontStatusMock).toHaveBeenCalledTimes(1))
        expect(updateStorefrontStatusMock).toHaveBeenCalledWith(expect.objectContaining({
            storefrontStatus: 'maintenance',
            maintenanceMessage: 'Accepted by backend',
            rowVersion: 'rv-1',
        }))
        expect(updateStorefrontStatusMock).not.toHaveBeenCalledWith(expect.objectContaining({
            commerceAi: expect.anything(),
        }))
        expect(updateStoreConfigMock).not.toHaveBeenCalled()

        await waitFor(() => expect(screen.getByText('Current Status').nextElementSibling).toHaveTextContent('Maintenance'))
        expect(screen.queryByText('Unsaved storefront settings are candidate-only until the backend accepts them.')).not.toBeInTheDocument()
        expect(showSuccessMock).toHaveBeenCalledWith('Storefront settings saved')
        expect(getStoreConfigMock).toHaveBeenCalledTimes(1)
    })

    it('does not replace baseline when the mutation fails or returns malformed acceptance evidence', async () => {
        getStoreConfigMock.mockResolvedValue(backendConfig())
        updateStorefrontStatusMock.mockResolvedValue(backendConfig({
            tenantId: '33333333-3333-4333-8333-333333333333',
            storefrontStatus: 'maintenance',
            rowVersion: 'rv-2',
        }))

        await renderSettings()

        await screen.findByText('Backend Store')
        fireEvent.click(screen.getByRole('radio', { name: /Maintenance/ }))
        fireEvent.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(updateStorefrontStatusMock).toHaveBeenCalledTimes(1))
        expect(screen.getByText('Current Status').nextElementSibling).toHaveTextContent('Live')
        expect(screen.getByText('Unsaved storefront settings are candidate-only until the backend accepts them.')).toBeInTheDocument()
        expect(showErrorMock).toHaveBeenCalledWith('Failed to save storefront settings')
        expect(showSuccessMock).not.toHaveBeenCalled()
    })

    it('suppresses duplicate save clicks while one mutation is in flight', async () => {
        getStoreConfigMock.mockResolvedValue(backendConfig())
        let resolveSave: (value: unknown) => void = () => {}
        updateStorefrontStatusMock.mockImplementation(() => new Promise((resolve) => {
            resolveSave = resolve
        }))

        await renderSettings()

        await screen.findByText('Backend Store')
        fireEvent.click(screen.getByRole('radio', { name: /Maintenance/ }))
        fireEvent.click(screen.getByRole('button', { name: 'Save' }))
        fireEvent.click(screen.getByRole('button', { name: 'Saving...' }))

        expect(updateStorefrontStatusMock).toHaveBeenCalledTimes(1)

        await act(async () => {
            resolveSave(backendConfig({ storefrontStatus: 'maintenance', rowVersion: 'rv-2' }))
        })

        await waitFor(() => expect(screen.getByText('Current Status').nextElementSibling).toHaveTextContent('Maintenance'))
    })
})

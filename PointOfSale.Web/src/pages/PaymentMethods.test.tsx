import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act } from 'react'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import PaymentMethods, { isUnsupportedMethod, mergePaymentMethodCatalog } from './PaymentMethods.jsx'
import AuthBootstrap from '../components/AuthBootstrap'
import ProtectedRoute from '../components/ProtectedRoute'
import { useAuthStore } from '../store/authStore'

const apiMock = vi.hoisted(() => ({
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    requestPosSessionRefresh: vi.fn(),
}))

vi.mock('../services/api', () => ({
    default: apiMock,
    requestPosSessionRefresh: apiMock.requestPosSessionRefresh,
    isDefinitiveAuthFailure: (error: { response?: { status?: number } }) => (
        error?.response?.status === 401 || error?.response?.status === 403
    ),
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (_key: string, fallback?: string, values?: Record<string, string>) => {
            if (fallback && values?.name) {
                return fallback.replace('{{name}}', values.name)
            }

            return fallback ?? _key
        },
        i18n: { language: 'en' },
    }),
}))

vi.mock('../contexts/SettingsContext', () => ({
    useSettings: () => ({
        formatCurrency: (value: number) => `$${value.toFixed(2)}`,
        settings: { currencySymbol: '$' },
    }),
}))

const providerCatalog = [
    { code: 'COD', displayName: 'Cash on Delivery', sortOrder: 1, supportsOnline: false, supportsOffline: true, requiresConfiguration: false, fee: 0, feeType: 'fixed' },
    { code: 'STRIPE', displayName: 'Credit/Debit Card', sortOrder: 2, supportsOnline: true, supportsOffline: false, requiresConfiguration: true, fee: 0, feeType: 'fixed' },
    { code: 'PAYMOB', displayName: 'Online Payment', sortOrder: 3, supportsOnline: true, supportsOffline: false, requiresConfiguration: true, fee: 0, feeType: 'fixed' },
    { code: 'FAWRY', displayName: 'Fawry Pay', sortOrder: 4, supportsOnline: true, supportsOffline: false, requiresConfiguration: true, fee: 0, feeType: 'fixed' },
    { code: 'MANUALWALLET', displayName: 'Manual Wallet', sortOrder: 5, supportsOnline: false, supportsOffline: true, requiresConfiguration: true, fee: 0, feeType: 'fixed' },
]

const authenticatedUser = {
    id: '11111111-1111-4111-8111-111111111111',
    tenantId: '22222222-2222-4222-8222-222222222222',
    username: 'owner',
    firstName: 'POS',
    lastName: 'Owner',
    role: 'Owner',
    effectivePermissions: ['storefront.payments.manage'],
    navigationCapabilities: [],
    vendorLifecycleAuthority: {
        vendorId: '22222222-2222-4222-8222-222222222222',
        status: 'Active',
        debtStatus: 'Current',
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
}

beforeEach(() => {
    useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: false })
    apiMock.requestPosSessionRefresh.mockReset()
})

afterEach(() => {
    vi.clearAllMocks()
})

describe('PaymentMethods provider catalog', () => {
    it('merges a backend provider catalog with zero tenant rows as not configured providers', () => {
        const methods = mergePaymentMethodCatalog(providerCatalog, [])

        expect(methods.map((method) => method.code)).toEqual(['COD', 'STRIPE', 'PAYMOB', 'FAWRY', 'MANUALWALLET'])
        expect(methods.every((method) => method.hasTenantPaymentMethod === false)).toBe(true)
        expect(methods.every((method) => method.isEnabled === false)).toBe(true)
    })

    it('renders all supported providers for a fresh tenant instead of an empty screen', async () => {
        apiMock.get.mockImplementation((url: string) => {
            if (url === '/admin/payment-methods/catalog') {
                return Promise.resolve({ data: providerCatalog })
            }

            return Promise.resolve({ data: [] })
        })

        render(<PaymentMethods />)

        expect(await screen.findByText('Available Payment Methods')).toBeInTheDocument()
        expect(screen.getByText('Cash on Delivery')).toBeInTheDocument()
        expect(screen.getByText('Credit/Debit Card')).toBeInTheDocument()
        expect(screen.getByText('Online Payment')).toBeInTheDocument()
        expect(screen.getByText('Fawry Pay')).toBeInTheDocument()
        expect(screen.getByText('Manual Wallet')).toBeInTheDocument()
        expect(screen.queryByText('No payment methods available')).not.toBeInTheDocument()
        // T-09: Fawry renders as unsupported instead of "Not configured".
        expect(screen.getAllByText('Not configured')).toHaveLength(9)
        expect(screen.getByText('Unsupported')).toBeInTheDocument()
        expect(screen.getByText('FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE')).toBeInTheDocument()
    })

    it('marks Fawry unsupported even when a stale tenant row says enabled', () => {
        const methods = mergePaymentMethodCatalog(providerCatalog, [
            { code: 'FAWRY', displayName: 'Fawry Pay', isEnabled: true, isSupported: true },
        ])

        const fawry = methods.find((method) => method.code === 'FAWRY')
        expect(fawry).toBeDefined()
        expect(isUnsupportedMethod(fawry)).toBe(true)
        expect(fawry?.isSupported).toBe(false)
    })

    it('exposes no configure or enable action for Fawry', async () => {
        apiMock.get.mockImplementation((url: string) => {
            if (url === '/admin/payment-methods/catalog') {
                return Promise.resolve({ data: providerCatalog })
            }

            return Promise.resolve({ data: [] })
        })

        render(<PaymentMethods />)

        const fawryRow = (await screen.findByText('Fawry Pay')).closest('tr')
        expect(fawryRow).not.toBeNull()
        expect(within(fawryRow as HTMLElement).queryByRole('button')).not.toBeInTheDocument()
    })

    it('configures COD without credentials through the existing tenant payment method mutation', async () => {
        apiMock.get.mockImplementation((url: string) => {
            if (url === '/admin/payment-methods/catalog') {
                return Promise.resolve({ data: providerCatalog })
            }

            return Promise.resolve({ data: [] })
        })
        apiMock.put.mockResolvedValue({ data: {} })
        const user = userEvent.setup()

        render(<PaymentMethods />)

        const codRow = (await screen.findByText('Cash on Delivery')).closest('tr')
        expect(codRow).not.toBeNull()

        await user.click(within(codRow as HTMLElement).getByRole('button', { name: 'Configure' }))

        await waitFor(() => {
            expect(apiMock.put).toHaveBeenCalledWith('/admin/payment-methods/COD', expect.objectContaining({
                configuration: null,
                displayName: 'Cash on Delivery',
            }))
        })
    })

    it('exposes Paymob API Key as a masked write-only configuration field', async () => {
        apiMock.get.mockImplementation((url: string) => {
            if (url === '/admin/payment-methods/catalog') {
                return Promise.resolve({ data: providerCatalog })
            }

            return Promise.resolve({ data: [] })
        })
        apiMock.put.mockResolvedValue({ data: {} })
        const user = userEvent.setup()

        render(<PaymentMethods />)

        const paymobRow = (await screen.findByText('Online Payment')).closest('tr')
        expect(paymobRow).not.toBeNull()

        await user.click(within(paymobRow as HTMLElement).getByRole('button', { name: 'Configure' }))

        const apiKeyInput = await screen.findByLabelText('API Key')
        expect(apiKeyInput).toHaveAttribute('type', 'password')
        expect(screen.getByText('Stored encrypted. Leave blank to keep the existing API Key.')).toBeInTheDocument()

        await user.type(screen.getByLabelText('Public Key'), 'egy_pk_test_placeholder')
        await user.type(apiKeyInput, 'paymob-dashboard-api-key')
        await user.type(screen.getByLabelText('Secret Key'), 'egy_sk_test_placeholder')
        await user.type(screen.getByLabelText('Integration IDs'), '3153090')
        await user.type(screen.getByLabelText('HMAC Secret'), 'paymob-hmac-secret')
        await user.click(screen.getByRole('button', { name: 'common.save' }))

        await waitFor(() => {
            expect(apiMock.put).toHaveBeenCalledWith('/admin/payment-methods/PAYMOB/configure', expect.objectContaining({
                configuration: expect.objectContaining({
                    apiKey: 'paymob-dashboard-api-key',
                    integrationId: '3153090',
                }),
            }))
        })
    })

    it('preserves Paymob API and secret keys across focus and visibility revalidation', async () => {
        let sessionRequestCount = 0
        let resolveLifecycleSession: ((value: unknown) => void) | undefined
        const lifecycleSession = new Promise((resolve) => {
            resolveLifecycleSession = resolve
        })

        apiMock.get.mockImplementation((url: string) => {
            if (url === '/auth/session') {
                sessionRequestCount += 1
                return sessionRequestCount === 1
                    ? Promise.resolve({ data: { user: authenticatedUser } })
                    : lifecycleSession
            }
            if (url === '/admin/payment-methods/catalog') {
                return Promise.resolve({ data: providerCatalog })
            }

            return Promise.resolve({ data: [] })
        })
        const user = userEvent.setup()

        render(
            <>
                <AuthBootstrap />
                <MemoryRouter initialEntries={['/ecommerce/payment-methods']}>
                    <Routes>
                        <Route element={<ProtectedRoute />}>
                            <Route path="/ecommerce/payment-methods" element={<PaymentMethods />} />
                        </Route>
                    </Routes>
                </MemoryRouter>
            </>,
        )

        const paymobRow = (await screen.findByText('Online Payment')).closest('tr')
        expect(paymobRow).not.toBeNull()
        await user.click(within(paymobRow as HTMLElement).getByRole('button', { name: 'Configure' }))
        await user.type(screen.getByLabelText('API Key'), 'copied-api-key')
        await user.type(screen.getByLabelText('Secret Key'), 'copied-secret-key')

        act(() => {
            window.dispatchEvent(new Event('blur'))
            window.dispatchEvent(new Event('focus'))
            document.dispatchEvent(new Event('visibilitychange'))
        })

        expect(screen.getByLabelText('API Key')).toHaveValue('copied-api-key')
        expect(screen.getByLabelText('Secret Key')).toHaveValue('copied-secret-key')
        expect(screen.queryByText('Restoring session...')).not.toBeInTheDocument()
        expect(sessionRequestCount).toBe(2)

        await act(async () => {
            resolveLifecycleSession?.({ data: { user: authenticatedUser } })
            await lifecycleSession
        })

        expect(screen.getByLabelText('API Key')).toHaveValue('copied-api-key')
        expect(screen.getByLabelText('Secret Key')).toHaveValue('copied-secret-key')
    })
})

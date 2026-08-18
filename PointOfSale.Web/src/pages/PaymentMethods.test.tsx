import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import PaymentMethods, { mergePaymentMethodCatalog } from './PaymentMethods.jsx'

const apiMock = vi.hoisted(() => ({
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
}))

vi.mock('../services/api', () => ({
    default: apiMock,
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
        expect(screen.getAllByText('Not configured')).toHaveLength(10)
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
})

import type { ReactNode } from 'react'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CheckoutPendingPage } from './fixtures/storefrontPendingPage.fixture'

const probes = vi.hoisted(() => ({
    status: vi.fn(), clearCart: vi.fn(), reset: vi.fn(), navigate: vi.fn(), clearContext: vi.fn(), retry: vi.fn(),
}))
const orderId = '11111111-1111-4111-8111-111111111111'

// The two applications have separate node_modules. Render this cross-app fixture
// with the test runner's React rather than creating a second hooks dispatcher.
vi.mock('../../../PointOfSale.Storefront/node_modules/react', async () => vi.importActual('react'))
vi.mock('../../../PointOfSale.Storefront/node_modules/react-router-dom', () => ({
    useSearchParams: () => [new URLSearchParams('orderId=11111111-1111-4111-8111-111111111111')],
}))
vi.mock('@/components/layout', () => ({ Layout: ({ children }: { children: ReactNode }) => <main>{children}</main> }))
vi.mock('@/components/common/AffiliateLink', () => ({ AffiliateLink: ({ children }: { children: ReactNode }) => <span>{children}</span> }))
vi.mock('@/hooks', () => ({ useCategories: () => ({ categories: [], isLoading: false }) }))
vi.mock('@/hooks/useAffiliateNavigation', () => ({ useAffiliateNavigate: () => probes.navigate }))
vi.mock('@/i18n', () => ({ useTranslation: () => ({ t: (key: string) => key }) }))
vi.mock('@/services/api', () => ({
    paymentStatusApi: { getCanonicalPaymentStatus: probes.status, retryPaymobPayment: probes.retry },
    normalizeError: () => ({ message: 'Controlled outage' }),
}))
vi.mock('@/store/cartStore', () => ({ useCartStore: { getState: () => ({ clearCart: probes.clearCart }) } }))
vi.mock('@/store/checkoutStore', () => ({ useCheckoutStore: { getState: () => ({ reset: probes.reset }) }, clearPersistedCheckoutStateStorage: vi.fn() }))
vi.mock('@/features/checkout/activeCheckoutOrderContext', () => ({
    readCheckoutTokenForOrder: () => null,
    readActiveCheckoutOrderContext: () => ({ orderId: '11111111-1111-4111-8111-111111111111' }),
    clearActiveCheckoutOrderContextIfMatches: probes.clearContext,
    clearCheckoutTokenForOrder: vi.fn(),
}))
vi.mock('@/features/checkout/checkoutTelemetry', () => ({
    emitGuestPaymentRecoverySuccess: vi.fn(), emitCartUnexpectedClearBlocked: vi.fn(), emitCheckoutFinalizeFailure: vi.fn(),
}))

function status(state: string) {
    return { orderId, orderNumber: 'SYNTHETIC', status: state, orderStatus: 'Pending', paymentStatus: state === 'PAID' ? 'Paid' : state === 'FAILED' ? 'Failed' : 'Pending', paymentMethod: 'PAYMOB', canRetryPayment: state === 'FAILED', totalAmount: 100, currency: 'EGP' }
}

describe('actual pending-payment page cart lifecycle', () => {
    beforeEach(() => { vi.clearAllMocks(); probes.clearCart.mockResolvedValue(true) })
    afterEach(() => cleanup())

    it.each(['PENDING', 'PROCESSING', 'MANUAL_REVIEW', 'FAILED'])('does not clear a cart or announce payment success for %s', async state => {
        probes.status.mockResolvedValue(status(state))
        render(<CheckoutPendingPage />)
        if (state === 'FAILED') await screen.findByRole('heading', { name: 'checkout.pending.failedTitle' })
        else await screen.findByText(new RegExp(`backendStatus.*${state}`))
        expect(probes.clearCart).not.toHaveBeenCalled()
        expect(probes.reset).not.toHaveBeenCalled()
        expect(probes.navigate).not.toHaveBeenCalled()
    })

    it('finalizes once only after the backend confirms PAID', async () => {
        probes.status.mockResolvedValue(status('PAID'))
        render(<CheckoutPendingPage />)
        await waitFor(() => expect(probes.clearCart).toHaveBeenCalledTimes(1))
        expect(probes.reset).toHaveBeenCalledTimes(1)
        expect(probes.navigate).toHaveBeenCalledWith(expect.stringContaining(`/orders/${orderId}/confirmation`), { replace: true })
    })

    it('preserves the cart through a polling failure and an explicit retry returning pending', async () => {
        probes.status.mockRejectedValueOnce(new Error('Controlled outage')).mockResolvedValue(status('PENDING'))
        render(<CheckoutPendingPage />)
        await screen.findByText('checkout.pending.pollingError')
        expect(probes.clearCart).not.toHaveBeenCalled()
        fireEvent.click(screen.getByRole('button', { name: 'checkout.pending.refreshStatus' }))
        await screen.findByText(/backendStatus.*PENDING/)
        expect(probes.status).toHaveBeenCalledTimes(2)
        expect(probes.clearCart).not.toHaveBeenCalled()
    })
})

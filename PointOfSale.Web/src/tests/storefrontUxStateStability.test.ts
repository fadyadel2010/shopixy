import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const STALE_SELECTION_MESSAGE = 'checkout.staleSelection'
const CART_MISMATCH_MESSAGE = 'We could not confirm the latest cart totals. Please retry.'

function createMemoryStorage() {
    const store = new Map<string, string>()

    return {
        getItem: (key: string) => (store.has(key) ? store.get(key)! : null),
        setItem: (key: string, value: string) => {
            store.set(key, value)
        },
        removeItem: (key: string) => {
            store.delete(key)
        },
        clear: () => {
            store.clear()
        },
        key: (index: number) => Array.from(store.keys())[index] ?? null,
        get length() {
            return store.size
        },
    }
}

function createDeferred<T>() {
    let resolve!: (value: T) => void
    let reject!: (reason?: unknown) => void

    const promise = new Promise<T>((promiseResolve, promiseReject) => {
        resolve = promiseResolve
        reject = promiseReject
    })

    return { promise, resolve, reject }
}

async function flushPromises() {
    // Drain the microtask queue more robustly for complex async chains
    for (let i = 0; i < 6; i++) {
        await Promise.resolve()
    }
}

function stubStorefrontGlobals() {
    const localStorage = createMemoryStorage()
    const sessionStorage = createMemoryStorage()

    vi.stubGlobal('localStorage', localStorage)
    vi.stubGlobal('sessionStorage', sessionStorage)
    vi.stubGlobal('navigator', { userAgent: 'vitest-storefront' })
    vi.stubGlobal('document', { referrer: '' })
    vi.stubGlobal('window', {
        location: {
            search: '',
            hostname: 'storefront.test',
        },
        confirm: vi.fn(() => true),
        open: vi.fn(),
        localStorage,
        sessionStorage,
        navigator: { userAgent: 'vitest-storefront' },
        crypto: globalThis.crypto,
    })
}

describe('storefront UX consistency and state stability', () => {
    beforeEach(() => {
        vi.resetModules()
        vi.clearAllMocks()
        stubStorefrontGlobals()
    })

    afterEach(() => {
        vi.useRealTimers()
        vi.unstubAllGlobals()
    })

    it('keeps the customer signed in when auth checks fail transiently and retries silently', async () => {
        vi.useFakeTimers()

        const customer = {
            id: 'customer-1',
            email: 'customer@example.com',
            displayName: 'Reliable Customer',
            provider: 'Local' as const,
            emailVerified: true,
            createdAtUtc: '2026-04-21T00:00:00.000Z',
        }

        let callCount = 0
        const getMe = vi.fn(async () => {
            callCount++
            if (callCount === 1) {
                throw new Error('network down')
            }
            return customer
        })
        const normalizeAuthError = vi.fn(() => ({
            code: 'NETWORK_ERROR',
            message: 'Network error. Please check your connection.',
        }))
        const clearSession = vi.fn()
        const setCustomerId = vi.fn()
        const emitAuthStateDegraded = vi.fn()

        vi.doMock('@/auth/api/authApi', () => ({
            getMe,
            normalizeAuthError,
            refreshToken: vi.fn(),
            login: vi.fn(),
            register: vi.fn(),
            logout: vi.fn(),
            googleLogin: vi.fn(),
            facebookLogin: vi.fn(),
            getProviders: vi.fn(),
        }))

        vi.doMock('@/bootstrap', () => ({
            clearSession,
            setCustomerId,
        }))

        vi.doMock('@/features/auth/authTelemetry', () => ({
            emitAuthStateDegraded,
            emitAuthDegradedRecovery: vi.fn(),
            emitCheckoutStateLeakPrevented: vi.fn(),
        }))

        const { useAuthStore } = await import('@/auth/store/authStore')

        useAuthStore.setState({
            status: 'authenticated',
            customer,
            isLoading: false,
            error: null,
        })

        await useAuthStore.getState().checkAuth()

        expect(useAuthStore.getState().status).toBe('degraded')
        expect(useAuthStore.getState().customer).toEqual(customer)
        expect(clearSession).not.toHaveBeenCalled()
        expect(setCustomerId).toHaveBeenCalledWith(null)
        expect(emitAuthStateDegraded).toHaveBeenCalledWith('check_auth', 'NETWORK_ERROR', true)

        await vi.advanceTimersByTimeAsync(5000)
        await flushPromises()

        expect(getMe).toHaveBeenCalledTimes(2)
        expect(useAuthStore.getState().status).toBe('authenticated')
        expect(useAuthStore.getState().customer).toEqual(customer)
        expect(setCustomerId).toHaveBeenLastCalledWith(customer.id)
    })

    it('preserves stale checkout selections and flags them instead of resetting the flow', async () => {
        const httpClient = {
            get: vi.fn(),
            post: vi.fn(),
            patch: vi.fn(),
        }
        const emitCheckoutSelectionInvalidated = vi.fn()

        vi.doMock('@/services/api', () => ({
            httpClient,
            normalizeError: vi.fn(() => ({ message: 'Request failed', isNetworkError: false })),
            isRetryableError: vi.fn(() => false),
            isRequestCanceled: vi.fn(() => false),
            fetchCart: vi.fn(),
            addCartItem: vi.fn(),
            updateCartItem: vi.fn(),
            removeCartItem: vi.fn(),
            updateCartItemByVariant: vi.fn(),
            removeCartItemByVariant: vi.fn(),
            clearCart: vi.fn(),
            applyCoupon: vi.fn(),
            removeCoupon: vi.fn(),
            applyOrderDraft: vi.fn(),
        }))

        vi.doMock('@/auth/store/authStore', () => {
            const authState = { status: 'authenticated' }
            return {
                useAuthStore: {
                    getState: () => authState,
                },
                selectIsAuthenticated: (state: typeof authState) => state.status === 'authenticated',
                selectAuthStatus: (state: typeof authState) => state.status,
            }
        })

        vi.doMock('@/features/checkout/activeCheckoutOrderContext', () => ({
            persistActiveCheckoutOrderContext: vi.fn(),
        }))

        vi.doMock('@/features/checkout/checkoutSelectionTelemetry', () => ({
            emitCheckoutSelectionInvalidated,
        }))

        vi.doMock('@/features/cart/cartTelemetry', () => ({
            emitCartStateMismatchDetected: vi.fn(),
        }))

        const { useCheckoutStore } = await import('@/store/checkoutStore')
        const { useCartStore } = await import('@/store/cartStore')

        useCartStore.setState({
            cart: {
                cartId: '11111111-1111-4111-8111-111111111111',
                tenantId: '22222222-2222-4222-8222-222222222222',
                ownerType: 'session' as const,
                ownerId: 'session-1',
                items: [],
                itemCount: 0,
                subtotal: 0,
                discountTotal: 0,
                taxTotal: 0,
                grandTotal: 0,
                currency: 'USD',
                lastUpdatedUtc: '2026-09-23T00:00:00Z',
                expiresAtUtc: '2026-09-24T00:00:00Z',
                appliedCouponCode: null,
                appliedCoupon: null,
            },
            isInitialized: true,
            isLoading: false,
        })

        useCheckoutStore.setState({
            step: 'review',
            checkoutSessionId: '33333333-3333-4333-8333-333333333333',
            checkoutSessionCartId: '11111111-1111-4111-8111-111111111111',
            manualWalletUploadToken: 'test-upload-token',
            shippingMethodId: '44444444-4444-4444-8444-444444444444',
            shippingMethods: [
                {
                    tenantId: '22222222-2222-4222-8222-222222222222',
                    id: '44444444-4444-4444-8444-444444444444',
                    name: 'Legacy Courier',
                    price: 25,
                    currency: 'USD',
                },
            ],
            shippingSelectionStatus: 'valid',
            staleShippingSelection: null,
            paymentMethod: 'COD',
            selectedPaymentMethodCode: 'COD',
            paymentMethods: [
                {
                    id: 'payment-cod',
                    code: 'COD',
                    displayName: 'Cash on Delivery',
                    supportsOnline: false,
                    supportsOffline: true,
                    fee: 0,
                    feeType: 'fixed',
                    sortOrder: 0,
                    isAvailable: true,
                },
            ],
            paymentSelectionStatus: 'valid',
            stalePaymentSelection: null,
        })

        httpClient.get
            .mockResolvedValueOnce({
                data: [
                    {
                        id: '66666666-6666-4666-8666-666666666666',
                        code: 'PAYMOB',
                        displayName: 'Paymob',
                        supportsOnline: true,
                        supportsOffline: false,
                        fee: 0,
                        feeType: 'fixed',
                        sortOrder: 0,
                        isAvailable: true,
                    },
                ],
            })
        httpClient.post.mockResolvedValueOnce({
            data: {
                tenantId: '22222222-2222-4222-8222-222222222222',
                shippingMethodsStatus: 'AVAILABLE',
                shippingMethods: [
                    {
                        tenantId: '22222222-2222-4222-8222-222222222222',
                        id: '55555555-5555-4555-8555-555555555555',
                        name: 'Express',
                        price: 35,
                        currency: 'USD',
                    },
                ],
            },
        })

        await useCheckoutStore.getState().fetchShippingMethods()
        await useCheckoutStore.getState().fetchPaymentMethods()

        const state = useCheckoutStore.getState()

        expect(state.step).toBe('review')
        expect(state.shippingMethodId).toBe('44444444-4444-4444-8444-444444444444')
        expect(state.shippingSelectionStatus).toBe('stale')
        expect(state.staleShippingSelection).toEqual({
            value: '44444444-4444-4444-8444-444444444444',
            label: 'Legacy Courier',
            message: STALE_SELECTION_MESSAGE,
        })
        expect(state.paymentMethod).toBe('COD')
        expect(state.selectedPaymentMethodCode).toBe('COD')
        expect(state.paymentSelectionStatus).toBe('stale')
        expect(state.stalePaymentSelection).toEqual({
            value: 'COD',
            label: 'Cash on Delivery',
            message: STALE_SELECTION_MESSAGE,
        })
        expect(emitCheckoutSelectionInvalidated).toHaveBeenCalledWith('shipping', 'missing_from_latest_config', '44444444-4444-4444-8444-444444444444')
        expect(emitCheckoutSelectionInvalidated).toHaveBeenCalledWith('payment', 'missing_from_latest_config', 'COD')
    })

    it('keeps cart items and totals stable while an authoritative mutation is pending and rejects mismatches', async () => {
        const pendingUpdate = createDeferred<typeof currentCart>()
        const updateCartItemByVariant = vi.fn(() => pendingUpdate.promise)
        const emitCartStateMismatchDetected = vi.fn()

        vi.doMock('@/services/api', () => ({
            fetchCart: vi.fn(),
            addCartItem: vi.fn(),
            updateCartItem: vi.fn(),
            removeCartItem: vi.fn(),
            updateCartItemByVariant,
            removeCartItemByVariant: vi.fn(),
            clearCart: vi.fn(),
            applyCoupon: vi.fn(),
            removeCoupon: vi.fn(),
            applyOrderDraft: vi.fn(),
        }))

        vi.doMock('@/features/cart/cartTelemetry', () => ({
            emitCartStateMismatchDetected,
        }))

        const { useCartStore } = await import('@/store/cartStore')

        const currentCart = {
            cartId: 'cart-1',
            tenantId: 'tenant-1',
            ownerType: 'session' as const,
            ownerId: 'session-1',
            items: [
                {
                    productId: 'product-1',
                    variantId: 'variant-1',
                    productName: 'Premium Beans',
                    sku: 'SYN-PREMIUM-BEANS',
                    quantity: 2,
                    unitPrice: 50,
                    lineTotal: 100,
                },
            ],
            itemCount: 2,
            subtotal: 100,
            discountTotal: 0,
            taxTotal: 0,
            grandTotal: 100,
            currency: 'USD',
            lastUpdatedUtc: '2026-04-21T00:00:00.000Z',
            expiresAtUtc: '2026-04-22T00:00:00.000Z',
            appliedCouponCode: null,
            appliedCoupon: null,
        }

        useCartStore.setState({
            cart: currentCart,
            isLoading: false,
            error: null,
            updatingItemId: null,
            isInitialized: true,
            couponError: null,
            isApplyingCoupon: false,
            hydrationSource: 'api',
            skipNextFetch: false,
        })

        const pendingMutation = useCartStore.getState().updateQuantity('product-1', 3, 'variant-1')

        expect(useCartStore.getState().updatingItemId).toBe('variant-1')
        expect(useCartStore.getState().cart?.items[0].quantity).toBe(2)
        expect(useCartStore.getState().cart?.itemCount).toBe(2)
        expect(useCartStore.getState().cart?.subtotal).toBe(100)
        expect(await useCartStore.getState().updateQuantity('product-1', 4, 'variant-1')).toBe(false)
        expect(updateCartItemByVariant).toHaveBeenCalledTimes(1)

        pendingUpdate.resolve({
            ...currentCart,
            items: [
                {
                    ...currentCart.items[0],
                    quantity: 3,
                    lineTotal: 150,
                },
            ],
            itemCount: 3,
            subtotal: 150,
            grandTotal: 150,
        })

        await expect(pendingMutation).resolves.toBe(true)
        expect(useCartStore.getState().cart?.items[0].quantity).toBe(3)
        expect(useCartStore.getState().cart?.itemCount).toBe(3)
        expect(useCartStore.getState().cart?.subtotal).toBe(150)
        expect(useCartStore.getState().updatingItemId).toBeNull()

        updateCartItemByVariant.mockResolvedValueOnce({
            ...currentCart,
            itemCount: 999,
        })

        await expect(useCartStore.getState().updateQuantity('product-1', 5, 'variant-1')).resolves.toBe(false)

        expect(useCartStore.getState().cart?.itemCount).toBe(3)
        expect(useCartStore.getState().error).toBe(CART_MISMATCH_MESSAGE)
        expect(emitCartStateMismatchDetected).toHaveBeenCalledWith('cart_store', 'item_count_mismatch')
    })
})

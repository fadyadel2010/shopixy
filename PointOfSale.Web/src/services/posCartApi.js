import api from './api'

const OPEN_CART_STATUSES = new Set([0, 4, 'Active', 'Recovered'])
const HELD_CART_STATUSES = new Set([1, 'Held'])

const buildRequestId = (operation) => {
    const suffix = typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
        ? crypto.randomUUID()
        : `${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
    return `pos-cart-${operation}-${suffix}`
}

const mutationConfig = (operation) => {
    const requestId = buildRequestId(operation)
    return {
        headers: {
            'Idempotency-Key': requestId,
            'X-Request-Id': requestId,
        },
    }
}

const normalizeId = (value) => String(value || '').trim().toLowerCase()

export const isPosCartId = (value) => (
    /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(String(value || '').trim())
)

const getLineKey = ({ productId, productVariantId, variantId, unitId }) => (
    `${normalizeId(productId)}::${normalizeId(productVariantId || variantId || 'default')}::${normalizeId(unitId || 'default')}`
)

const isNotFound = (error) => error?.response?.status === 404

export async function getPosCart(cartId, { signal } = {}) {
    if (!isPosCartId(cartId)) {
        return null
    }

    try {
        const response = await api.get(`/pos-carts/${cartId}`, { signal })
        return response.data
    } catch (error) {
        if (isNotFound(error)) {
            return null
        }
        throw error
    }
}

export async function getOpenPosCart({ signal } = {}) {
    const response = await api.get('/pos-carts/open', { signal })
    return response.data ?? null
}

export async function createPosCart({ warehouseId, customerId = null, signal } = {}) {
    const response = await api.post('/pos-carts', {
        warehouseId,
        customerId: customerId || null,
        expiresInMinutes: 480,
    }, {
        ...mutationConfig('create'),
        signal,
    })
    return response.data
}

export async function transitionPosCart(cart, transition, reason, { signal } = {}) {
    if (!cart?.cartId || !Number.isInteger(cart.version)) {
        return null
    }

    try {
        const response = await api.post(`/pos-carts/${cart.cartId}/${transition}`, {
            expectedVersion: cart.version,
            reason,
        }, {
            ...mutationConfig(transition),
            signal,
        })
        return response.data
    } catch (error) {
        if (isNotFound(error)) {
            return null
        }
        throw error
    }
}

export async function cancelPosCart(cartId, reason = 'POS cart cleared', { signal } = {}) {
    const cart = await getPosCart(cartId, { signal })
    if (!cart || (!OPEN_CART_STATUSES.has(cart.status) && !HELD_CART_STATUSES.has(cart.status))) {
        return null
    }

    return transitionPosCart(cart, 'cancel', reason, { signal })
}

const createOrRecoverCart = async ({ cartId, warehouseId, customerId, signal }) => {
    let cart = await getPosCart(cartId, { signal })
    if (!cart) {
        cart = await getOpenPosCart({ signal })
    }

    if (cart && new Date(cart.expiresAtUtc).getTime() <= Date.now()) {
        await transitionPosCart(cart, 'expire', 'Expired during POS browser recovery', { signal })
        cart = null
    }

    const desiredCustomerId = customerId || null
    const hasMatchingContext = cart &&
        normalizeId(cart.warehouseId) === normalizeId(warehouseId) &&
        normalizeId(cart.customerId) === normalizeId(desiredCustomerId)

    if (cart && (!hasMatchingContext || (!OPEN_CART_STATUSES.has(cart.status) && !HELD_CART_STATUSES.has(cart.status)))) {
        if (OPEN_CART_STATUSES.has(cart.status) || HELD_CART_STATUSES.has(cart.status)) {
            await transitionPosCart(cart, 'cancel', 'POS browser context changed', { signal })
        }
        cart = null
    }

    if (cart && HELD_CART_STATUSES.has(cart.status)) {
        cart = await transitionPosCart(cart, 'resume', 'POS browser cart restored', { signal })
    }

    if (cart && OPEN_CART_STATUSES.has(cart.status)) {
        return cart
    }

    try {
        return await createPosCart({ warehouseId, customerId: desiredCustomerId, signal })
    } catch (error) {
        if (error?.response?.status !== 409) {
            throw error
        }

        const concurrentCart = await getOpenPosCart({ signal })
        if (!concurrentCart) {
            throw error
        }

        if (new Date(concurrentCart.expiresAtUtc).getTime() <= Date.now()) {
            await transitionPosCart(concurrentCart, 'expire', 'Expired during concurrent POS cart recovery', { signal })
            return createPosCart({ warehouseId, customerId: desiredCustomerId, signal })
        }

        const concurrentContextMatches =
            normalizeId(concurrentCart.warehouseId) === normalizeId(warehouseId) &&
            normalizeId(concurrentCart.customerId) === normalizeId(desiredCustomerId)

        if (!concurrentContextMatches || (!OPEN_CART_STATUSES.has(concurrentCart.status) && !HELD_CART_STATUSES.has(concurrentCart.status))) {
            if (OPEN_CART_STATUSES.has(concurrentCart.status) || HELD_CART_STATUSES.has(concurrentCart.status)) {
                await transitionPosCart(concurrentCart, 'cancel', 'Concurrent POS browser context changed', { signal })
            }
            return createPosCart({ warehouseId, customerId: desiredCustomerId, signal })
        }

        if (HELD_CART_STATUSES.has(concurrentCart.status)) {
            return transitionPosCart(concurrentCart, 'resume', 'Concurrent POS browser cart restored', { signal })
        }

        return concurrentCart
    }
}

const updateExistingLine = async (cart, serverItem, desiredItem, signal) => {
    const quantity = Number(desiredItem.quantity)
    const discountAmount = Number(desiredItem.discountAmount || 0)
    if (
        Number(serverItem.quantity) === quantity &&
        Number(serverItem.discountAmount) === discountAmount
    ) {
        return cart
    }

    const response = await api.put(
        `/pos-carts/${cart.cartId}/items/${serverItem.itemId}`,
        { quantity, discountAmount, expectedVersion: cart.version },
        { ...mutationConfig('update-item'), signal },
    )
    return response.data
}

const addDesiredLine = async (cart, desiredItem, signal) => {
    const response = await api.post(`/pos-carts/${cart.cartId}/items`, {
        productId: desiredItem.productId,
        productVariantId: desiredItem.variantId || desiredItem.productVariantId || null,
        unitId: desiredItem.unitId || null,
        quantity: Number(desiredItem.quantity),
        discountAmount: Number(desiredItem.discountAmount || 0),
        expectedVersion: cart.version,
    }, {
        ...mutationConfig('add-item'),
        signal,
    })
    return response.data
}

const removeServerLine = async (cart, serverItem, signal) => {
    const response = await api.delete(`/pos-carts/${cart.cartId}/items/${serverItem.itemId}`, {
        ...mutationConfig('remove-item'),
        data: {
            expectedVersion: cart.version,
            reason: 'Removed from POS browser cart',
        },
        signal,
    })
    return response.data
}

export async function synchronizePosCart({ cartId, warehouseId, customerId = null, items, signal } = {}) {
    const desiredItems = Array.isArray(items) ? items.filter(item => item?.productId && Number(item.quantity) > 0) : []
    if (!warehouseId) {
        throw new Error('A warehouse is required before synchronizing a POS cart.')
    }
    if (desiredItems.length === 0) {
        throw new Error('At least one item is required before synchronizing a POS cart.')
    }

    let cart = await createOrRecoverCart({ cartId, warehouseId, customerId, signal })
    const desiredKeys = new Set(desiredItems.map(getLineKey))

    for (const desiredItem of desiredItems) {
        const desiredKey = getLineKey(desiredItem)
        const existing = (cart.items || []).find(item => getLineKey(item) === desiredKey)
        cart = existing
            ? await updateExistingLine(cart, existing, desiredItem, signal)
            : await addDesiredLine(cart, desiredItem, signal)
    }

    for (const serverItem of [...(cart.items || [])]) {
        if (!desiredKeys.has(getLineKey(serverItem))) {
            cart = await removeServerLine(cart, serverItem, signal)
        }
    }

    return cart
}

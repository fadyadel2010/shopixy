import {
    getScopedStorageKey,
    getTenantStorageScope,
    getUserStorageScope,
    normalizeStorageSegment,
} from './storageScope'

const POS_CART_STORAGE_PREFIX = 'pos_cart_v2'
const POS_CART_SCHEMA_VERSION = 2
const POS_CART_MAX_AGE_MS = 24 * 60 * 60 * 1000
const POS_CART_MAX_STORAGE_SIZE = 4 * 1024 * 1024
const POS_CART_SESSION_SCOPE_KEY = 'pos_cart_scope_v1'

const LEGACY_POS_CART_STORAGE_PREFIX = 'pos_cart_v1'
const LEGACY_GLOBAL_CART_STORAGE_KEY = LEGACY_POS_CART_STORAGE_PREFIX
const LEGACY_CART_STORAGE_KEYS = ['pos_cart_backup']

let runtimeCartSessionScope = null

/**
 * Safely access sessionStorage without throwing SecurityError.
 * Returns undefined if access fails.
 */
const safeSessionStorageGetItem = (key) => {
    try {
        if (typeof sessionStorage !== 'undefined') {
            return sessionStorage.getItem(key)
        }
    } catch (error) {
        // SecurityError or other storage access errors - fail silently
        console.warn('sessionStorage access failed:', error.message)
    }
    return undefined
}

/**
 * Safely write to sessionStorage without throwing SecurityError.
 * Returns true if successful, false otherwise.
 */
const safeSessionStorageSetItem = (key, value) => {
    try {
        if (typeof sessionStorage !== 'undefined') {
            sessionStorage.setItem(key, value)
            return true
        }
    } catch (error) {
        // SecurityError or other storage access errors - fail silently
        console.warn('sessionStorage write failed:', error.message)
    }
    return false
}

const isPlainObject = (value) => value != null && typeof value === 'object' && !Array.isArray(value)

const toSafeNumber = (value, fallback = 0) => {
    const parsed = typeof value === 'number' ? value : Number(value)
    return Number.isFinite(parsed) ? parsed : fallback
}

const sanitizeCustomer = (customer) => {
    if (customer == null) {
        return null
    }

    if (!isPlainObject(customer) || !customer.id || typeof customer.name !== 'string') {
        return null
    }

    return {
        ...customer,
        id: customer.id,
        name: customer.name,
    }
}

const sanitizeProductUnits = (productUnits) => (
    Array.isArray(productUnits)
        ? productUnits
            .filter(unit => isPlainObject(unit) && unit.unitId)
            .map(unit => ({
                ...unit,
                unitId: unit.unitId,
                unitName: typeof unit.unitName === 'string' ? unit.unitName : '',
                unitSymbol: typeof unit.unitSymbol === 'string' ? unit.unitSymbol : '',
                conversionFactor: toSafeNumber(unit.conversionFactor, 1),
                sellingPrice: toSafeNumber(unit.sellingPrice),
                purchasePrice: toSafeNumber(unit.purchasePrice),
                isDefault: Boolean(unit.isDefault),
            }))
        : []
)

const sanitizeVariantAttributes = (variantAttributes) => (
    Array.isArray(variantAttributes)
        ? variantAttributes
            .filter(attribute => isPlainObject(attribute))
            .map(attribute => ({
                ...attribute,
                name: typeof attribute.name === 'string' ? attribute.name : '',
                value: typeof attribute.value === 'string' ? attribute.value : '',
            }))
        : []
)

const sanitizeItems = (items) => {
    if (!Array.isArray(items)) {
        return []
    }

    return items
        .filter(item => isPlainObject(item) && item.productId && typeof item.name === 'string')
        .map(item => ({
            ...item,
            productId: item.productId,
            variantId: item.variantId || null,
            unitId: item.unitId || null,
            name: item.name,
            barcode: typeof item.barcode === 'string' ? item.barcode : '',
            sku: typeof item.sku === 'string' ? item.sku : '',
            unitName: typeof item.unitName === 'string' ? item.unitName : '',
            unit: typeof item.unit === 'string' ? item.unit : '',
            quantity: Math.max(0, toSafeNumber(item.quantity)),
            conversionFactor: Math.max(1, toSafeNumber(item.conversionFactor, 1)),
            unitPrice: Math.max(0, toSafeNumber(item.unitPrice)),
            taxRate: Math.max(0, toSafeNumber(item.taxRate)),
            stockQuantity: Math.max(0, toSafeNumber(item.stockQuantity)),
            discountAmount: Math.max(0, toSafeNumber(item.discountAmount)),
            priceIncludesTax: Boolean(item.priceIncludesTax),
            images: item.images ?? null,
            productUnits: sanitizeProductUnits(item.productUnits),
            variantAttributes: sanitizeVariantAttributes(item.variantAttributes),
            variantDisplayName: typeof item.variantDisplayName === 'string' ? item.variantDisplayName : null,
        }))
}

const sanitizeDiscountType = (discountType) => (
    discountType === 'fixed' ? 'fixed' : 'percentage'
)

const sanitizeSnapshot = (snapshot) => {
    if (!isPlainObject(snapshot)) {
        return null
    }

    const items = sanitizeItems(snapshot.items)
    if (items.length === 0) {
        return null
    }

    return {
        cartId: typeof snapshot.cartId === 'string' ? normalizeStorageSegment(snapshot.cartId, '') : '',
        items,
        customer: sanitizeCustomer(snapshot.customer),
        discountType: sanitizeDiscountType(snapshot.discountType),
        discountValue: Math.max(0, toSafeNumber(snapshot.discountValue)),
        warehouseId: snapshot.warehouseId || '',
        editingSaleId: snapshot.editingSaleId || null,
        editingInvoiceNumber: snapshot.editingInvoiceNumber || null,
        editingOriginalItems: sanitizeItems(snapshot.editingOriginalItems),
        editingMutationVersion: Number.isInteger(snapshot.editingMutationVersion) && snapshot.editingMutationVersion >= 0
            ? snapshot.editingMutationVersion
            : null,
        savedAt: typeof snapshot.savedAt === 'string' ? snapshot.savedAt : new Date().toISOString(),
    }
}

const getSharedCartStorageKey = () => getScopedStorageKey(POS_CART_STORAGE_PREFIX, 'active')

const getOrCreateCartSessionScope = () => {
    if (runtimeCartSessionScope) {
        return runtimeCartSessionScope
    }

    // Try to read existing scope from sessionStorage first
    const existingScope = safeSessionStorageGetItem(POS_CART_SESSION_SCOPE_KEY)
    
    // Validate the stored scope matches expected pattern: tab_<timestamp>_<rand>
    // Pattern: "tab_" followed by alphanumeric base36 timestamp, "_", then 6 alphanumeric chars
    const scopePattern = /^tab_[a-z0-9]+_[a-z0-9]{6}$/
    
    if (existingScope && scopePattern.test(existingScope)) {
        runtimeCartSessionScope = existingScope
        return runtimeCartSessionScope
    }

    // Generate new scope if none exists or stored value is invalid
    runtimeCartSessionScope = `tab_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 8)}`

    // Persist the new scope to sessionStorage
    safeSessionStorageSetItem(POS_CART_SESSION_SCOPE_KEY, runtimeCartSessionScope)

    return runtimeCartSessionScope
}

const getCartEnvelope = (snapshot, sourceId = getOrCreateCartSessionScope()) => ({
    version: POS_CART_SCHEMA_VERSION,
    savedAt: new Date().toISOString(),
    sourceId: normalizeStorageSegment(sourceId, 'tab_default'),
    snapshot,
})

const isEnvelopeExpired = (savedAt) => {
    const savedAtMs = new Date(savedAt).getTime()

    if (!Number.isFinite(savedAtMs)) {
        return true
    }

    return (Date.now() - savedAtMs) > POS_CART_MAX_AGE_MS
}

const parseEnvelopeValue = (rawValue, expectedVersion) => {
    if (!rawValue) {
        return null
    }

    try {
        const parsedValue = JSON.parse(rawValue)
        if (!isPlainObject(parsedValue) || parsedValue.version !== expectedVersion) {
            return null
        }

        if (isEnvelopeExpired(parsedValue.savedAt)) {
            return null
        }

        const sanitizedSnapshot = sanitizeSnapshot(parsedValue.snapshot)
        if (!sanitizedSnapshot) {
            return null
        }

        return {
            version: parsedValue.version,
            savedAt: parsedValue.savedAt,
            sourceId: typeof parsedValue.sourceId === 'string' ? parsedValue.sourceId : null,
            snapshot: sanitizedSnapshot,
        }
    } catch (error) {
        console.error('Error parsing POS cart snapshot:', error)
        return null
    }
}

const getLegacyStoragePrefix = () => (
    `${LEGACY_POS_CART_STORAGE_PREFIX}:${getTenantStorageScope()}:${getUserStorageScope()}:`
)

const clearLegacySnapshots = () => {
    if (typeof localStorage === 'undefined') {
        return
    }

    localStorage.removeItem(LEGACY_GLOBAL_CART_STORAGE_KEY)
    LEGACY_CART_STORAGE_KEYS.forEach((key) => {
        localStorage.removeItem(key)
    })

    const legacyStoragePrefix = getLegacyStoragePrefix()
    for (let index = localStorage.length - 1; index >= 0; index -= 1) {
        const storageKey = localStorage.key(index)
        if (storageKey?.startsWith(legacyStoragePrefix)) {
            localStorage.removeItem(storageKey)
        }
    }
}

const migrateLegacySnapshot = () => {
    if (typeof localStorage === 'undefined') {
        return null
    }

    const sharedStorageKey = getSharedCartStorageKey()
    const existingSharedValue = localStorage.getItem(sharedStorageKey)
    
    // Validate existing shared value before clearing legacy data
    if (existingSharedValue) {
        const parsedEnvelope = parseEnvelopeValue(existingSharedValue, POS_CART_SCHEMA_VERSION)
        
        // If validation succeeds, clear legacy snapshots and return the valid value
        if (parsedEnvelope) {
            clearLegacySnapshots()
            return existingSharedValue
        }
        
        // If validation fails, remove the bad shared key and continue to load v1 snapshots
        // so getPersistedPosCart() can recover from legacy data
        localStorage.removeItem(sharedStorageKey)
    }

    const candidateEnvelopes = []
    const legacyStoragePrefix = getLegacyStoragePrefix()

    for (let index = 0; index < localStorage.length; index += 1) {
        const storageKey = localStorage.key(index)
        if (!storageKey?.startsWith(legacyStoragePrefix)) {
            continue
        }

        const candidateEnvelope = parseEnvelopeValue(localStorage.getItem(storageKey), 1)
        if (candidateEnvelope) {
            candidateEnvelopes.push(candidateEnvelope)
        }
    }

    for (const legacyKey of [LEGACY_GLOBAL_CART_STORAGE_KEY, ...LEGACY_CART_STORAGE_KEYS]) {
        const candidateEnvelope = parseEnvelopeValue(localStorage.getItem(legacyKey), 1)
        if (candidateEnvelope) {
            candidateEnvelopes.push(candidateEnvelope)
        }
    }

    if (candidateEnvelopes.length === 0) {
        clearLegacySnapshots()
        return null
    }

    const latestEnvelope = candidateEnvelopes
        .slice()
        .sort((left, right) => new Date(right.savedAt).getTime() - new Date(left.savedAt).getTime())[0]

    const migratedEnvelope = getCartEnvelope(latestEnvelope.snapshot)
    localStorage.setItem(sharedStorageKey, JSON.stringify(migratedEnvelope))
    clearLegacySnapshots()
    return localStorage.getItem(sharedStorageKey)
}

export const buildPosCartSnapshotFingerprint = (snapshot) => {
    const sanitized = sanitizeSnapshot(snapshot)
    if (!sanitized) {
        return JSON.stringify(null)
    }

    // Normalize savedAt to make fingerprint deterministic
    // Remove the timestamp since it changes on every sanitization
    const normalized = { ...sanitized, savedAt: null }
    return JSON.stringify(normalized)
}

export const shouldApplyExternalPosCartSnapshot = ({
    localHasActiveCart,
    localFingerprint,
    externalFingerprint,
}) => {
    if (externalFingerprint === localFingerprint) {
        return true
    }

    return !localHasActiveCart
}

export const savePosCartSnapshot = (snapshot, { sourceId = getOrCreateCartSessionScope() } = {}) => {
    if (typeof localStorage === 'undefined') {
        return
    }

    try {
        const sanitizedSnapshot = sanitizeSnapshot(snapshot)
        const sharedStorageKey = getSharedCartStorageKey()

        if (!sanitizedSnapshot) {
            localStorage.removeItem(sharedStorageKey)
            clearLegacySnapshots()
            return
        }

        const serializedEnvelope = JSON.stringify(getCartEnvelope(sanitizedSnapshot, sourceId))
        if (serializedEnvelope.length > POS_CART_MAX_STORAGE_SIZE) {
            console.warn('POS cart snapshot exceeded the safe localStorage budget and was cleared.')
            localStorage.removeItem(sharedStorageKey)
            clearLegacySnapshots()
            return
        }

        localStorage.setItem(sharedStorageKey, serializedEnvelope)
        clearLegacySnapshots()
    } catch (error) {
        console.error('Error saving POS cart snapshot:', error)
    }
}

export const getPersistedPosCart = () => {
    if (typeof localStorage === 'undefined') {
        return null
    }

    const sharedStorageKey = getSharedCartStorageKey()
    const rawValue = localStorage.getItem(sharedStorageKey) ?? migrateLegacySnapshot()
    const parsedEnvelope = parseEnvelopeValue(rawValue, POS_CART_SCHEMA_VERSION)

    if (!parsedEnvelope) {
        localStorage.removeItem(sharedStorageKey)
        return null
    }

    return parsedEnvelope.snapshot
}

export const subscribeToPosCartChanges = (callback) => {
    if (typeof window === 'undefined') {
        return () => {}
    }

    const sharedStorageKey = getSharedCartStorageKey()

    const handleStorage = (event) => {
        if (event.key !== sharedStorageKey) {
            return
        }

        if (!event.newValue) {
            callback?.({
                sourceId: null,
                savedAt: null,
                snapshot: null,
            })
            return
        }

        const parsedEnvelope = parseEnvelopeValue(event.newValue, POS_CART_SCHEMA_VERSION)

        callback?.({
            sourceId: parsedEnvelope?.sourceId ?? null,
            savedAt: parsedEnvelope?.savedAt ?? null,
            snapshot: parsedEnvelope?.snapshot ?? null,
        })
    }

    window.addEventListener('storage', handleStorage)
    return () => window.removeEventListener('storage', handleStorage)
}

export const clearPersistedPosCart = () => {
    if (typeof localStorage === 'undefined') {
        return
    }

    localStorage.removeItem(getSharedCartStorageKey())
    clearLegacySnapshots()
}

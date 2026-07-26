import { getScopedStorageKey, getTenantStorageScope, getUserStorageScope } from '../utils/storageScope'

export const POS_RUNTIME_CACHE_TTL_MS = 30 * 1000
export const POS_RUNTIME_CACHE_MAX_ENTRIES = 200
export const POS_RUNTIME_CACHE_MAX_BYTES = 5 * 1024 * 1024

const POS_RUNTIME_INVALIDATION_PREFIX = 'pos_runtime_invalidation_v1'
const POS_RUNTIME_INVALIDATION_EVENT = 'pos-runtime-cache:invalidated'

const cacheEntries = new Map()
const requestSeqByKey = new Map()
const activeRequestCountByKey = new Map()
const sizeEstimateCache = new WeakMap()

let cacheGeneration = 0
let cacheApproxBytes = 0
let cleanupTimerId = null
let scheduledExpiry = null

const AVERAGE_BYTES_PER_KEY = 50
const STRINGIFY_SIZE_THRESHOLD = 100000

const getInvalidationStorageKey = () => getScopedStorageKey(POS_RUNTIME_INVALIDATION_PREFIX, 'all')
const getRuntimeCacheScope = () => `${getTenantStorageScope()}:${getUserStorageScope()}`
const getScopedRuntimeCacheKey = (key) => `${getRuntimeCacheScope()}:${key}`

const getTimestamp = () => Date.now()

const createInvalidationPayload = (reason = 'manual') => ({
    reason,
    timestamp: getTimestamp(),
    token: `${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 10)}`,
})

const dispatchInvalidationEvent = (payload) => {
    if (typeof window === 'undefined') {
        return
    }

    window.dispatchEvent(new CustomEvent(POS_RUNTIME_INVALIDATION_EVENT, { detail: payload }))
}

const clearRuntimeCache = () => {
    cacheGeneration += 1
    cacheEntries.clear()
    requestSeqByKey.clear()
    activeRequestCountByKey.clear()
    cacheApproxBytes = 0

    if (cleanupTimerId != null && typeof clearTimeout === 'function') {
        clearTimeout(cleanupTimerId)
        cleanupTimerId = null
        scheduledExpiry = null
    }
}

const estimateCacheEntryBytes = (key, data) => {
    const keyBytes = key.length

    // Handle primitives quickly
    if (data === null || data === undefined) {
        return keyBytes + 4 // "null" or "undefined"
    }

    const dataType = typeof data
    if (dataType === 'boolean') {
        return keyBytes + 5 // "true" or "false"
    }

    if (dataType === 'number') {
        return keyBytes + String(data).length
    }

    if (dataType === 'string') {
        return keyBytes + data.length
    }

    // For objects and arrays, check WeakMap cache first
    if (dataType === 'object') {
        const cachedSize = sizeEstimateCache.get(data)
        if (cachedSize !== undefined) {
            return keyBytes + cachedSize
        }

        try {
            // For small objects, use JSON.stringify
            if (Array.isArray(data)) {
                if (data.length === 0) {
                    const size = 2 // "[]"
                    sizeEstimateCache.set(data, size)
                    return keyBytes + size
                }

                // Rough estimate for arrays
                if (data.length > 100) {
                    const size = data.length * AVERAGE_BYTES_PER_KEY
                    sizeEstimateCache.set(data, size)
                    return keyBytes + size
                }
            } else {
                const keys = Object.keys(data)
                if (keys.length === 0) {
                    const size = 2 // "{}"
                    sizeEstimateCache.set(data, size)
                    return keyBytes + size
                }

                // Rough estimate for large objects
                if (keys.length > 50) {
                    const size = keys.length * AVERAGE_BYTES_PER_KEY
                    sizeEstimateCache.set(data, size)
                    return keyBytes + size
                }
            }

            // For moderate-sized objects, try JSON.stringify with size check
            const jsonString = JSON.stringify(data)
            const size = jsonString.length

            // Only cache if below threshold to avoid memory bloat
            if (size < STRINGIFY_SIZE_THRESHOLD) {
                sizeEstimateCache.set(data, size)
            }

            return keyBytes + size
        } catch {
            // Fallback: rough estimate based on object structure
            try {
                const keys = Object.keys(data)
                const size = keys.length * AVERAGE_BYTES_PER_KEY
                sizeEstimateCache.set(data, size)
                return keyBytes + size
            } catch {
                return keyBytes
            }
        }
    }

    // Fallback for functions or other types
    return keyBytes
}

const deleteCacheEntry = (scopedKey) => {
    const existingEntry = cacheEntries.get(scopedKey)
    if (!existingEntry) {
        return false
    }

    cacheApproxBytes = Math.max(0, cacheApproxBytes - (existingEntry.sizeBytes || 0))
    cacheEntries.delete(scopedKey)
    return true
}

const markRequestStarted = (scopedKey) => {
    activeRequestCountByKey.set(scopedKey, (activeRequestCountByKey.get(scopedKey) || 0) + 1)
}

const markRequestFinished = (scopedKey) => {
    const remaining = Math.max(0, (activeRequestCountByKey.get(scopedKey) || 0) - 1)
    if (remaining === 0) {
        activeRequestCountByKey.delete(scopedKey)
        return
    }

    activeRequestCountByKey.set(scopedKey, remaining)
}

const pruneRequestSequences = () => {
    const maxSequenceEntries = POS_RUNTIME_CACHE_MAX_ENTRIES * 2
    if (requestSeqByKey.size <= maxSequenceEntries) {
        return
    }

    for (const scopedKey of requestSeqByKey.keys()) {
        if (requestSeqByKey.size <= maxSequenceEntries) {
            break
        }

        if (!cacheEntries.has(scopedKey) && !activeRequestCountByKey.has(scopedKey)) {
            requestSeqByKey.delete(scopedKey)
        }
    }
}

const setRequestSequence = (scopedKey, sequence) => {
    if (requestSeqByKey.has(scopedKey)) {
        requestSeqByKey.delete(scopedKey)
    }

    requestSeqByKey.set(scopedKey, sequence)
}

const setCacheEntry = (scopedKey, data, ttlMs) => {
    const now = getTimestamp()
    deleteCacheEntry(scopedKey)

    const entry = {
        data,
        timestamp: now,
        expiresAt: now + ttlMs,
        sizeBytes: estimateCacheEntryBytes(scopedKey, data),
    }

    cacheApproxBytes += entry.sizeBytes
    cacheEntries.set(scopedKey, entry)
    
    // If this entry expires before the currently scheduled cleanup, reschedule
    if (scheduledExpiry != null && entry.expiresAt < scheduledExpiry) {
        if (cleanupTimerId != null && typeof clearTimeout === 'function') {
            clearTimeout(cleanupTimerId)
            cleanupTimerId = null
            scheduledExpiry = null
        }
        scheduleRuntimeCacheCleanup()
    }
}

const touchCacheEntry = (scopedKey, entry) => {
    cacheEntries.delete(scopedKey)
    cacheEntries.set(scopedKey, entry)
}

const evictRuntimeCacheIfNeeded = ({
    maxEntries = POS_RUNTIME_CACHE_MAX_ENTRIES,
    maxBytes = POS_RUNTIME_CACHE_MAX_BYTES,
} = {}) => {
    const normalizedMaxEntries = Math.max(1, maxEntries)
    const normalizedMaxBytes = Math.max(1024, maxBytes)
    const now = getTimestamp()

    for (const [scopedKey, entry] of cacheEntries.entries()) {
        if (entry.expiresAt <= now) {
            deleteCacheEntry(scopedKey)
        }
    }

    const maxIterations = 1000
    let iterations = 0
    let previousApproxBytes = cacheApproxBytes

    while (cacheEntries.size > normalizedMaxEntries || cacheApproxBytes > normalizedMaxBytes) {
        const oldestKey = cacheEntries.keys().next().value
        if (!oldestKey) {
            break
        }

        deleteCacheEntry(oldestKey)

        // Safety check: ensure progress is being made
        iterations += 1
        if (iterations >= maxIterations) {
            console.warn(
                `POS runtime cache eviction loop exceeded ${maxIterations} iterations. ` +
                `Entries: ${cacheEntries.size}, Bytes: ${cacheApproxBytes}. Breaking to prevent infinite loop.`
            )
            break
        }

        // Check if bytes are decreasing; if not, we may have corrupted state
        if (cacheApproxBytes >= previousApproxBytes && cacheApproxBytes > normalizedMaxBytes) {
            console.warn(
                `POS runtime cache eviction not reducing byte count (was ${previousApproxBytes}, now ${cacheApproxBytes}). ` +
                `Breaking to prevent infinite loop. This may indicate entries with sizeBytes === 0 or cache corruption.`
            )
            break
        }

        previousApproxBytes = cacheApproxBytes
    }

    pruneRequestSequences()
}

const scheduleRuntimeCacheCleanup = () => {
    if (cacheEntries.size === 0 || typeof setTimeout !== 'function') {
        return
    }

    const nextExpiry = Math.min(...Array.from(cacheEntries.values()).map(entry => entry.expiresAt))
    
    // If we already have a timer scheduled for an earlier or equal time, keep it
    if (cleanupTimerId != null && scheduledExpiry != null && scheduledExpiry <= nextExpiry) {
        return
    }
    
    // Clear existing timer if we need to reschedule for an earlier time
    if (cleanupTimerId != null && typeof clearTimeout === 'function') {
        clearTimeout(cleanupTimerId)
        cleanupTimerId = null
        scheduledExpiry = null
    }
    
    const delayMs = Math.max(1, nextExpiry - getTimestamp())

    cleanupTimerId = setTimeout(() => {
        cleanupTimerId = null
        scheduledExpiry = null
        evictRuntimeCacheIfNeeded()

        if (cacheEntries.size > 0) {
            scheduleRuntimeCacheCleanup()
        }
    }, delayMs)
    
    scheduledExpiry = nextExpiry

    cleanupTimerId?.unref?.()
}

const parseInvalidationPayload = (rawValue) => {
    if (!rawValue) {
        return createInvalidationPayload('external')
    }

    try {
        const parsedValue = JSON.parse(rawValue)
        return {
            reason: typeof parsedValue?.reason === 'string' ? parsedValue.reason : 'external',
            timestamp: Number.isFinite(parsedValue?.timestamp) ? parsedValue.timestamp : getTimestamp(),
        }
    } catch {
        return createInvalidationPayload('external')
    }
}

export const readPosRuntimeCache = async (
    key,
    loader,
    {
        ttlMs = POS_RUNTIME_CACHE_TTL_MS,
        force = false,
        maxEntries = POS_RUNTIME_CACHE_MAX_ENTRIES,
        maxBytes = POS_RUNTIME_CACHE_MAX_BYTES,
    } = {},
) => {
    const scopedKey = getScopedRuntimeCacheKey(key)
    const now = getTimestamp()
    const existingEntry = cacheEntries.get(scopedKey)

    if (!force && existingEntry && existingEntry.expiresAt > now) {
        touchCacheEntry(scopedKey, existingEntry)
        return existingEntry.data
    }

    if (existingEntry) {
        deleteCacheEntry(scopedKey)
    }

    // Increment per-key sequence to track concurrent requests
    const currentSeq = (requestSeqByKey.get(scopedKey) || 0) + 1
    setRequestSequence(scopedKey, currentSeq)
    markRequestStarted(scopedKey)

    const requestGeneration = cacheGeneration
    const requestSeq = currentSeq

    return Promise.resolve()
        .then(loader)
        .then((data) => {
            // Only write to cache if this is still the latest request for this key
            // and the cache hasn't been invalidated
            if (requestGeneration === cacheGeneration && requestSeq === requestSeqByKey.get(scopedKey)) {
                setCacheEntry(scopedKey, data, ttlMs)
                evictRuntimeCacheIfNeeded({ maxEntries, maxBytes })
                scheduleRuntimeCacheCleanup()
            }

            return data
        })
        .finally(() => {
            markRequestFinished(scopedKey)
            pruneRequestSequences()
        })
}

export const getPosRuntimeCacheStats = () => ({
    entries: cacheEntries.size,
    requestSequences: requestSeqByKey.size,
    activeRequests: activeRequestCountByKey.size,
    approxBytes: cacheApproxBytes,
    generation: cacheGeneration,
})

export const invalidatePosRuntimeCache = ({ reason = 'manual', broadcast = true } = {}) => {
    clearRuntimeCache()

    const payload = createInvalidationPayload(reason)
    dispatchInvalidationEvent(payload)

    if (broadcast && typeof localStorage !== 'undefined') {
        try {
            localStorage.setItem(getInvalidationStorageKey(), JSON.stringify(payload))
        } catch (error) {
            console.error('Error broadcasting POS runtime cache invalidation:', error)
        }
    }

    return payload
}

export const subscribeToPosRuntimeInvalidation = (callback) => {
    if (typeof window === 'undefined') {
        return () => {}
    }

    const handleInvalidation = (event) => {
        callback?.(event.detail || createInvalidationPayload('local'))
    }

    const handleStorage = (event) => {
        if (event.key !== getInvalidationStorageKey()) {
            return
        }

        clearRuntimeCache()
        callback?.(parseInvalidationPayload(event.newValue))
    }

    window.addEventListener(POS_RUNTIME_INVALIDATION_EVENT, handleInvalidation)
    window.addEventListener('storage', handleStorage)

    return () => {
        window.removeEventListener(POS_RUNTIME_INVALIDATION_EVENT, handleInvalidation)
        window.removeEventListener('storage', handleStorage)
    }
}

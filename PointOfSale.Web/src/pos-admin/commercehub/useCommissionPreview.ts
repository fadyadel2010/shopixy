import { useEffect, useMemo, useRef, useState } from 'react'
import api from '../../services/api'
import { useDebouncedValue } from '../../hooks/useDebouncedValue'
import {
    calculateCommissionBreakdown,
    type CommissionBreakdown,
} from './commissionCalculator'

interface CommissionPreviewApiResponse {
    vendorId?: string | null
    categoryId?: string | null
    rate?: number | null
    source?: string | null
    ruleId?: string | null
    priority?: number | null
    usedLegacyFallback?: boolean
    cacheStrategy?: string | null
}

interface UseCommissionPreviewInput {
    vendorId?: string | null
    categoryId?: string | null
    price?: number | null
    debounceMs?: number
}

export interface UseCommissionPreviewResult extends Omit<CommissionBreakdown, 'rate'> {
    rate: number | null
    source: string | null
    ruleId: string | null
    priority: number | null
    loading: boolean
    error: string | null
    hasCategory: boolean
    usedLegacyFallback: boolean
    usingEstimate: boolean
}

interface CommissionPreviewResolution {
    rate: number
    source: string
    ruleId: string | null
    priority: number | null
    usedLegacyFallback: boolean
    cacheStrategy: string | null
}

interface CommissionPreviewRequestIdentity {
    vendorId: string | null
    categoryId: string | null
}

const DEFAULT_DEBOUNCE_MS = 400
const resolutionCache = new Map<string, CommissionPreviewResolution>()
const defaultEstimateCache = new Map<string, CommissionPreviewResolution>()

function createCacheKey(vendorId?: string | null, categoryId?: string | null): string {
    return `${vendorId ?? ''}::${categoryId ?? ''}`
}

function normalizeResolution(
    response: CommissionPreviewApiResponse | null | undefined,
): CommissionPreviewResolution {
    return {
        rate: Number(response?.rate ?? 0),
        source: response?.source?.trim() || 'Legacy',
        ruleId: response?.ruleId ?? null,
        priority: response?.priority ?? null,
        usedLegacyFallback: Boolean(response?.usedLegacyFallback),
        cacheStrategy: response?.cacheStrategy ?? null,
    }
}

function getFallbackEstimate(
    vendorId?: string | null,
    categoryId?: string | null,
): CommissionPreviewResolution | null {
    const exactMatch = resolutionCache.get(createCacheKey(vendorId, categoryId))
    if (exactMatch) {
        return exactMatch
    }

    if (vendorId) {
        return defaultEstimateCache.get(vendorId) ?? null
    }

    return null
}

export function useCommissionPreview({
    vendorId,
    categoryId,
    price,
    debounceMs = DEFAULT_DEBOUNCE_MS,
}: UseCommissionPreviewInput): UseCommissionPreviewResult {
    const [resolution, setResolution] = useState<CommissionPreviewResolution | null>(null)
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState<string | null>(null)
    const [usingEstimate, setUsingEstimate] = useState(false)
    const requestIdRef = useRef(0)
    const requestIdentity = useMemo<CommissionPreviewRequestIdentity>(
        () => ({
            vendorId: vendorId ?? null,
            categoryId: categoryId ?? null,
        }),
        [categoryId, vendorId],
    )
    const debouncedRequestIdentity = useDebouncedValue(requestIdentity, debounceMs)

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    useEffect(() => {
        requestIdRef.current += 1

        if (!requestIdentity.categoryId) {
            setLoading(false)
            setError(null)
            setUsingEstimate(false)
            setResolution(null)
            return
        }

        setResolution(null)
        setError(null)
        setUsingEstimate(false)
        setLoading(true)
    }, [requestIdentity])

    useEffect(() => {
        const { vendorId: requestVendorId, categoryId: requestCategoryId } = debouncedRequestIdentity
        const hasCategory = Boolean(requestCategoryId)

        if (!hasCategory) {
            return undefined
        }

        const requestKey = createCacheKey(requestVendorId, requestCategoryId)
        const cached = resolutionCache.get(requestKey)
        if (cached) {
            setResolution(cached)
            setLoading(false)
            setError(null)
            setUsingEstimate(false)
            return undefined
        }

        const controller = new AbortController()
        const requestId = ++requestIdRef.current
        setLoading(true)
        setError(null)
        setUsingEstimate(false)

        async function fetchResolution() {
            try {
                const response = await api.get<CommissionPreviewApiResponse>(
                    '/admin/commercehub/commission/resolve-preview',
                    {
                        params: {
                            vendorId: requestVendorId,
                            categoryId: requestCategoryId,
                        },
                        signal: controller.signal,
                    },
                )

                const nextResolution = normalizeResolution(response.data)
                resolutionCache.set(requestKey, nextResolution)

                if (
                    requestVendorId &&
                    (nextResolution.source === 'Global' || nextResolution.usedLegacyFallback)
                ) {
                    defaultEstimateCache.set(requestVendorId, nextResolution)
                }

                if (requestId !== requestIdRef.current) {
                    return
                }

                setResolution(nextResolution)
                setUsingEstimate(false)
                setError(null)
            } catch {
                if (controller.signal.aborted) {
                    return
                }

                const fallbackResolution = getFallbackEstimate(requestVendorId, requestCategoryId)

                if (requestId !== requestIdRef.current) {
                    return
                }

                if (fallbackResolution) {
                    setResolution(fallbackResolution)
                    setUsingEstimate(true)
                    setError('Unable to fetch commission. Using default estimate.')
                } else {
                    setResolution(null)
                    setUsingEstimate(false)
                    setError('Unable to fetch commission preview right now.')
                }
            } finally {
                if (requestId === requestIdRef.current) {
                    setLoading(false)
                }
            }
        }

        void fetchResolution()

        return () => {
            controller.abort()
        }
    }, [debouncedRequestIdentity])

    const breakdown = useMemo(
        () => calculateCommissionBreakdown(price, resolution?.rate ?? 0),
        [price, resolution?.rate],
    )

    return {
        ...breakdown,
        rate: resolution?.rate ?? null,
        source: resolution?.source ?? null,
        ruleId: resolution?.ruleId ?? null,
        priority: resolution?.priority ?? null,
        loading,
        error,
        hasCategory: Boolean(categoryId),
        usedLegacyFallback: resolution?.usedLegacyFallback ?? false,
        usingEstimate,
    }
}

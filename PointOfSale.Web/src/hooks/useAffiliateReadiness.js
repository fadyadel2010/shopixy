/**
 * AF-23: Affiliate Readiness Hook
 * 
 * Checks if the tenant has completed all prerequisites to use affiliate features:
 * 1. Storefront status = Live
 * 2. At least one active payment method
 * 3. At least one active shipping method
 * 
 * RULES:
 * - Fetches from GET /api/admin/affiliate/readiness
 * - Returns loading state + readiness data
 * - Error fallback → treat as NOT READY (defensive)
 * - No caching (always fresh check)
 */
import { useState, useEffect, useCallback, useRef } from 'react'
import api from '../services/api'
import { AFFILIATE_ADMIN_READ_HEADERS } from '../services/adminAffiliateApi'

export function useAffiliateReadiness() {
    const [loading, setLoading] = useState(true)
    const [data, setData] = useState(null)
    const [error, setError] = useState(null)
    const requestSeq = useRef(0)

    const checkReadiness = useCallback(async ({ showLoading = true } = {}) => {
        const currentRequest = ++requestSeq.current

        try {
            if (showLoading) {
                setLoading(true)
            }

            const response = await api.get('/admin/affiliate/readiness', {
                headers: AFFILIATE_ADMIN_READ_HEADERS,
            })

            if (currentRequest === requestSeq.current) {
                setData(response.data)
                setError(null)
            }
        } catch (err) {
            if (currentRequest === requestSeq.current) {
                setError(err)
                // Defensive: treat errors as NOT READY
                setData({
                    isReady: false,
                    storefrontLive: false,
                    hasPaymentMethod: false,
                    hasShippingMethod: false,
                    missingRequirements: ['Unable to check affiliate readiness']
                })
            }
        } finally {
            if (currentRequest === requestSeq.current) {
                setLoading(false)
            }
        }
    }, [])

    useEffect(() => {
        let mounted = true

        const runIfMounted = (options) => {
            if (mounted) {
                void checkReadiness(options)
            }
        }

        runIfMounted()

        const handleVisibility = () => {
            if (document.visibilityState === 'visible') {
                runIfMounted({ showLoading: false })
            }
        }

        const handleLifecycleRevalidation = () => runIfMounted({ showLoading: false })

        window.addEventListener('focus', handleLifecycleRevalidation)
        window.addEventListener('online', handleLifecycleRevalidation)
        document.addEventListener('visibilitychange', handleVisibility)

        return () => {
            mounted = false
            requestSeq.current += 1
            window.removeEventListener('focus', handleLifecycleRevalidation)
            window.removeEventListener('online', handleLifecycleRevalidation)
            document.removeEventListener('visibilitychange', handleVisibility)
        }
    }, [checkReadiness])

    return {
        loading,
        isReady: data?.isReady ?? false,
        storefrontLive: data?.storefrontLive ?? false,
        hasPaymentMethod: data?.hasPaymentMethod ?? false,
        hasShippingMethod: data?.hasShippingMethod ?? false,
        missingRequirements: data?.missingRequirements ?? [],
        error,
        refetch: checkReadiness,
    }
}

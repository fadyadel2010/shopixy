/**
 * Analytics Funnels API Hook
 * Phase 5A: Admin Funnel Dashboard UI
 * 
 * ARCHITECTURAL RULES:
 * - Frontend is a PURE RENDERER
 * - Backend is the SINGLE SOURCE OF TRUTH
 * - NO funnel math in frontend
 * - NO derived calculations
 * 
 * All data comes from:
 * - GET /api/admin/analytics/funnels
 * - GET /api/admin/analytics/funnels/{key}/realtime
 * - GET /api/admin/analytics/funnels/{key}/compare
 */

import { useState, useEffect, useCallback } from 'react'
import api from '../../../services/api'
import { getRequestErrorMessage, useLatestRequestGuard } from '../../../hooks/useLatestRequestGuard'

/**
 * Hook to fetch funnel definitions
 */
export function useFunnelDefinitions() {
    const [definitions, setDefinitions] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const fetchDefinitions = useCallback(async () => {
        const requestId = beginRequest()

        try {
            setLoading(true)
            setError(null)
            const response = await api.get('/admin/analytics/funnels')

            if (!isLatestRequest(requestId)) {
                return
            }

            setDefinitions(response.data)
        } catch (err) {
            if (!isLatestRequest(requestId)) {
                return
            }

            console.error('[Funnels] Failed to fetch definitions:', err)
            setError(getRequestErrorMessage(err, 'Failed to load funnels'))
        } finally {
            if (isLatestRequest(requestId)) {
                setLoading(false)
            }
        }
    }, [beginRequest, isLatestRequest])

    useEffect(() => {
        fetchDefinitions()
    }, [fetchDefinitions])

    return { definitions, loading, error, refetch: fetchDefinitions }
}

/**
 * Hook to fetch realtime funnel data
 * @param {string} funnelKey - The funnel key
 * @param {number} windowMinutes - Time window (15, 30, 60, 180, 360, 720, 1440)
 */
export function useFunnelRealtime(funnelKey, windowMinutes = 60) {
    const [result, setResult] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const [lastUpdated, setLastUpdated] = useState(null)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const fetchFunnel = useCallback(async () => {
        if (!funnelKey) return

        const requestId = beginRequest()

        try {
            setLoading(true)
            setError(null)
            const response = await api.get(`/admin/analytics/funnels/${funnelKey}/realtime`, {
                params: { windowMinutes }
            })

            if (!isLatestRequest(requestId)) {
                return
            }

            setResult(response.data)
            setLastUpdated(new Date())
        } catch (err) {
            if (!isLatestRequest(requestId)) {
                return
            }

            console.error('[Funnels] Failed to fetch realtime data:', err)
            setError(getRequestErrorMessage(err, 'Failed to load funnel data'))
        } finally {
            if (isLatestRequest(requestId)) {
                setLoading(false)
            }
        }
    }, [beginRequest, funnelKey, isLatestRequest, windowMinutes])

    useEffect(() => {
        fetchFunnel()
    }, [fetchFunnel])

    return { result, loading, error, lastUpdated, refetch: fetchFunnel }
}

/**
 * Hook to fetch all funnels at once
 * @param {number} windowMinutes - Time window
 */
export function useAllFunnelsRealtime(windowMinutes = 60) {
    const [results, setResults] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const [lastUpdated, setLastUpdated] = useState(null)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const fetchAllFunnels = useCallback(async () => {
        const requestId = beginRequest()

        try {
            setLoading(true)
            setError(null)
            const response = await api.get('/admin/analytics/funnels/all/realtime', {
                params: { windowMinutes }
            })

            if (!isLatestRequest(requestId)) {
                return
            }

            setResults(response.data)
            setLastUpdated(new Date())
        } catch (err) {
            if (!isLatestRequest(requestId)) {
                return
            }

            console.error('[Funnels] Failed to fetch all funnels:', err)
            setError(getRequestErrorMessage(err, 'Failed to load funnel data'))
        } finally {
            if (isLatestRequest(requestId)) {
                setLoading(false)
            }
        }
    }, [beginRequest, isLatestRequest, windowMinutes])

    useEffect(() => {
        fetchAllFunnels()
    }, [fetchAllFunnels])

    return { results, loading, error, lastUpdated, refetch: fetchAllFunnels }
}

/**
 * Hook to compare a funnel across time windows
 * @param {string} funnelKey - The funnel key
 */
export function useFunnelComparison(funnelKey) {
    const [comparison, setComparison] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const fetchComparison = useCallback(async () => {
        if (!funnelKey) return

        const requestId = beginRequest()

        try {
            setLoading(true)
            setError(null)
            const response = await api.get(`/admin/analytics/funnels/${funnelKey}/compare`)

            if (!isLatestRequest(requestId)) {
                return
            }

            setComparison(response.data)
        } catch (err) {
            if (!isLatestRequest(requestId)) {
                return
            }

            console.error('[Funnels] Failed to fetch comparison:', err)
            setError(getRequestErrorMessage(err, 'Failed to load comparison'))
        } finally {
            if (isLatestRequest(requestId)) {
                setLoading(false)
            }
        }
    }, [beginRequest, funnelKey, isLatestRequest])

    useEffect(() => {
        fetchComparison()
    }, [fetchComparison])

    return { comparison, loading, error, refetch: fetchComparison }
}

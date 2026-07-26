/**
 * Attribution API Hooks
 * Phase 8A: Admin Attribution Dashboard UI
 * 
 * ARCHITECTURAL RULES:
 * - Frontend is a PURE RENDERER
 * - Backend is the SINGLE SOURCE OF TRUTH
 * - NO attribution math in frontend
 * - NO derived calculations
 * 
 * All data comes from:
 * - GET /api/admin/analytics/attribution/summary
 * - GET /api/admin/analytics/attribution/campaigns
 * - GET /api/admin/analytics/attribution/orders
 * - GET /api/admin/analytics/attribution/models
 */

import { useState, useEffect, useCallback } from 'react'
import api from '../../../../services/api'
import { getRequestErrorMessage, useLatestRequestGuard } from '../../../../hooks/useLatestRequestGuard'

/**
 * Hook to fetch attribution summary with revenue by source
 * @param {string} model - Attribution model ('FirstTouch' or 'LastTouch')
 * @param {string} period - Time period (today, last_7_days, last_30_days, etc.)
 */
export function useAttributionSummary(model = 'LastTouch', period = 'last_7_days') {
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const fetchSummary = useCallback(async () => {
        const requestId = beginRequest()

        try {
            setLoading(true)
            setError(null)
            const response = await api.get('/admin/analytics/attribution/summary', {
                params: { model, period }
            })

            if (!isLatestRequest(requestId)) {
                return
            }

            setData(response.data)
        } catch (err) {
            if (!isLatestRequest(requestId)) {
                return
            }

            console.error('[Attribution] Failed to fetch summary:', err)
            setError(getRequestErrorMessage(err, 'Failed to load attribution data'))
        } finally {
            if (isLatestRequest(requestId)) {
                setLoading(false)
            }
        }
    }, [beginRequest, isLatestRequest, model, period])

    useEffect(() => {
        fetchSummary()
    }, [fetchSummary])

    return { data, loading, error, refetch: fetchSummary }
}

/**
 * Hook to fetch campaign performance data
 * @param {string} model - Attribution model ('FirstTouch' or 'LastTouch')
 * @param {string} period - Time period
 */
export function useAttributionCampaigns(model = 'LastTouch', period = 'last_7_days') {
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const fetchCampaigns = useCallback(async () => {
        const requestId = beginRequest()

        try {
            setLoading(true)
            setError(null)
            const response = await api.get('/admin/analytics/attribution/campaigns', {
                params: { model, period }
            })

            if (!isLatestRequest(requestId)) {
                return
            }

            setData(response.data)
        } catch (err) {
            if (!isLatestRequest(requestId)) {
                return
            }

            console.error('[Attribution] Failed to fetch campaigns:', err)
            setError(getRequestErrorMessage(err, 'Failed to load campaign data'))
        } finally {
            if (isLatestRequest(requestId)) {
                setLoading(false)
            }
        }
    }, [beginRequest, isLatestRequest, model, period])

    useEffect(() => {
        fetchCampaigns()
    }, [fetchCampaigns])

    return { data, loading, error, refetch: fetchCampaigns }
}

/**
 * Hook to fetch orders with attribution data
 * @param {string} model - Attribution model
 * @param {string} period - Time period
 * @param {number} skip - Pagination offset
 * @param {number} take - Page size
 */
export function useAttributionOrders(model = 'LastTouch', period = 'last_7_days', skip = 0, take = 50) {
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const fetchOrders = useCallback(async () => {
        const requestId = beginRequest()

        try {
            setLoading(true)
            setError(null)
            const response = await api.get('/admin/analytics/attribution/orders', {
                params: { model, period, skip, take }
            })

            if (!isLatestRequest(requestId)) {
                return
            }

            setData(response.data)
        } catch (err) {
            if (!isLatestRequest(requestId)) {
                return
            }

            console.error('[Attribution] Failed to fetch orders:', err)
            setError(getRequestErrorMessage(err, 'Failed to load order attribution data'))
        } finally {
            if (isLatestRequest(requestId)) {
                setLoading(false)
            }
        }
    }, [beginRequest, isLatestRequest, model, period, skip, take])

    useEffect(() => {
        fetchOrders()
    }, [fetchOrders])

    return { data, loading, error, refetch: fetchOrders }
}

/**
 * Hook to compare attribution models (First Touch vs Last Touch)
 * @param {string} period - Time period
 */
export function useAttributionModels(period = 'last_7_days') {
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const fetchModels = useCallback(async () => {
        const requestId = beginRequest()

        try {
            setLoading(true)
            setError(null)
            const response = await api.get('/admin/analytics/attribution/models', {
                params: { period }
            })

            if (!isLatestRequest(requestId)) {
                return
            }

            setData(response.data)
        } catch (err) {
            if (!isLatestRequest(requestId)) {
                return
            }

            console.error('[Attribution] Failed to fetch model comparison:', err)
            setError(getRequestErrorMessage(err, 'Failed to load model comparison'))
        } finally {
            if (isLatestRequest(requestId)) {
                setLoading(false)
            }
        }
    }, [beginRequest, isLatestRequest, period])

    useEffect(() => {
        fetchModels()
    }, [fetchModels])

    return { data, loading, error, refetch: fetchModels }
}

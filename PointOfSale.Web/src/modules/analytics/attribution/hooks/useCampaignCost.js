/**
 * Campaign Cost & ROI API Hooks
 * Phase 8B: Campaign Cost & ROI (MVP)
 * 
 * ARCHITECTURAL RULES:
 * - Frontend is a PURE RENDERER
 * - Backend is the SINGLE SOURCE OF TRUTH
 * - NO ROI calculations in frontend
 * - NO cost logic in UI
 * 
 * All data comes from:
 * - GET /api/admin/analytics/campaign-costs
 * - POST /api/admin/analytics/campaign-costs
 * - DELETE /api/admin/analytics/campaign-costs/{id}
 * - GET /api/admin/analytics/roi/summary
 * - GET /api/admin/analytics/roi/campaigns
 */

import { useState, useEffect, useCallback } from 'react'
import api from '../../../../services/api'
import { getRequestErrorMessage, useLatestRequestGuard } from '../../../../hooks/useLatestRequestGuard'

/**
 * Hook to fetch campaign costs
 * @param {string} period - Time period (today, last_7_days, last_30_days, etc.)
 */
export function useCampaignCosts(period = 'last_30_days') {
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const fetchCosts = useCallback(async () => {
        const requestId = beginRequest()

        try {
            setLoading(true)
            setError(null)
            setData(null)
            const response = await api.get('/admin/analytics/campaign-costs', {
                params: { period }
            })

            if (!isLatestRequest(requestId)) {
                return
            }

            setData(response.data)
            return true
        } catch (err) {
            if (!isLatestRequest(requestId)) {
                return
            }

            console.error('[CampaignCost] Failed to fetch costs:', err)
            setError(getRequestErrorMessage(err, 'Failed to load campaign costs'))
            return false
        } finally {
            if (isLatestRequest(requestId)) {
                setLoading(false)
            }
        }
    }, [beginRequest, isLatestRequest, period])

    useEffect(() => {
        fetchCosts()
    }, [fetchCosts])

    return { data, loading, error, refetch: fetchCosts }
}

/**
 * Hook to create a new campaign cost
 */
export function useCreateCampaignCost() {
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState(null)

    const createCost = useCallback(async (costData) => {
        try {
            setLoading(true)
            setError(null)
            const response = await api.post('/admin/analytics/campaign-costs', costData)
            return response.data
        } catch (err) {
            const message = err.response?.data?.message || 'Failed to create campaign cost'
            setError(message)
            throw new Error(message)
        } finally {
            setLoading(false)
        }
    }, [])

    return { createCost, loading, error }
}

/**
 * Hook to update a campaign cost
 */
export function useUpdateCampaignCost() {
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState(null)

    const updateCost = useCallback(async (id, costData, revision) => {
        try {
            setLoading(true)
            setError(null)
            const response = await api.put(`/admin/analytics/campaign-costs/${id}`, costData, { headers: revision ? { 'If-Match': revision } : {} })
            return response.data
        } catch (err) {
            const message = err.response?.data?.message || 'Failed to update campaign cost'
            setError(message)
            throw new Error(message)
        } finally {
            setLoading(false)
        }
    }, [])

    return { updateCost, loading, error }
}

/**
 * Hook to delete a campaign cost
 */
export function useDeleteCampaignCost() {
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState(null)

    const deleteCost = useCallback(async (id) => {
        try {
            setLoading(true)
            setError(null)
            await api.delete(`/admin/analytics/campaign-costs/${id}`)
            return true
        } catch (err) {
            const message = err.response?.data?.message || 'Failed to delete campaign cost'
            setError(message)
            throw new Error(message)
        } finally {
            setLoading(false)
        }
    }, [])

    return { deleteCost, loading, error }
}

/**
 * Hook to fetch ROI summary by source
 * @param {string} model - Attribution model ('FirstTouch' or 'LastTouch')
 * @param {string} period - Time period
 */
export function useRoiSummary(model = 'LastTouch', period = 'last_30_days') {
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const fetchRoi = useCallback(async () => {
        const requestId = beginRequest()

        try {
            setLoading(true)
            setError(null)
            const response = await api.get('/admin/analytics/roi/summary', {
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

            console.error('[ROI] Failed to fetch summary:', err)
            setError(getRequestErrorMessage(err, 'Failed to load ROI summary'))
            setData(null)
        } finally {
            if (isLatestRequest(requestId)) {
                setLoading(false)
            }
        }
    }, [beginRequest, isLatestRequest, model, period])

    useEffect(() => {
        fetchRoi()
    }, [fetchRoi])

    return { data, loading, error, refetch: fetchRoi }
}

/**
 * Hook to fetch ROI by campaign
 * @param {string} model - Attribution model ('FirstTouch' or 'LastTouch')
 * @param {string} period - Time period
 */
export function useRoiByCampaign(model = 'LastTouch', period = 'last_30_days') {
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const fetchRoi = useCallback(async () => {
        const requestId = beginRequest()

        try {
            setLoading(true)
            setError(null)
            const response = await api.get('/admin/analytics/roi/campaigns', {
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

            console.error('[ROI] Failed to fetch campaigns:', err)
            setError(getRequestErrorMessage(err, 'Failed to load campaign ROI'))
            setData(null)
        } finally {
            if (isLatestRequest(requestId)) {
                setLoading(false)
            }
        }
    }, [beginRequest, isLatestRequest, model, period])

    useEffect(() => {
        fetchRoi()
    }, [fetchRoi])

    return { data, loading, error, refetch: fetchRoi }
}

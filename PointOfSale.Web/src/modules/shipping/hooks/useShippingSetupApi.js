/**
 * Shipping Setup API Hooks
 * Phase: Unified Shipping Configuration
 * 
 * Provides all API calls for the unified Shipping Setup page.
 * 
 * SINGLE SOURCE OF TRUTH - All shipping configuration from one place
 * 
 * APIs:
 * - GET /api/admin/carriers - Carrier configurations (read-only status)
 * - GET /api/admin/carriers/supported - All supported carriers
 * - PUT /api/admin/carriers/:code - Update carrier config
 * - POST /api/admin/carriers/:code/validate - Test connection
 * - GET /api/shippingmethods - Shipping options (customer-facing)
 * - POST/PUT/DELETE /api/shippingmethods - Manage shipping options
 */

import { useState, useEffect, useCallback, useRef } from 'react'
import api from '../../../services/api'

/**
 * Hook for fetching all carrier configurations and their status
 */
export function useCarrierConfigurations() {
    const [carriers, setCarriers] = useState([])
    const [supportedCarriers, setSupportedCarriers] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const requestIdRef = useRef(0)

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    const fetchCarriers = useCallback(async () => {
        const requestId = ++requestIdRef.current
        setLoading(true)
        setError(null)

        try {
            // Fetch both configured and supported carriers
            const [configResponse, supportedResponse] = await Promise.all([
                api.get('/admin/carriers'),
                api.get('/admin/carriers/supported')
            ])

            if (requestId !== requestIdRef.current) {
                return
            }

            setCarriers(configResponse.data || [])
            setSupportedCarriers(supportedResponse.data || [])
        } catch (err) {
            if (requestId !== requestIdRef.current) {
                return
            }

            console.error('Failed to fetch carriers:', err)
            setError(err.response?.data?.message || 'Failed to load carrier configurations')
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }, [])

    useEffect(() => {
        fetchCarriers()
    }, [fetchCarriers])

    return {
        carriers,
        supportedCarriers,
        loading,
        error,
        refetch: fetchCarriers
    }
}

/**
 * Hook for carrier configuration actions
 */
export function useCarrierActions() {
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState(null)
    const pendingRequestsRef = useRef(0)
    const latestActionIdRef = useRef(0)
    const isMountedRef = useRef(true)

    useEffect(() => {
        return () => {
            isMountedRef.current = false
            pendingRequestsRef.current = 0
            latestActionIdRef.current = 0
        }
    }, [])

    const beginLoading = useCallback(() => {
        const actionId = latestActionIdRef.current + 1
        latestActionIdRef.current = actionId
        pendingRequestsRef.current += 1

        if (isMountedRef.current) {
            setLoading(true)
            setError(null)
        }
        return actionId
    }, [])

    const endLoading = useCallback((actionId) => {
        pendingRequestsRef.current = Math.max(0, pendingRequestsRef.current - 1)

        if (isMountedRef.current && (actionId === latestActionIdRef.current || pendingRequestsRef.current === 0)) {
            setLoading(pendingRequestsRef.current > 0)
        }
    }, [])

    const commitError = useCallback((actionId, message) => {
        if (isMountedRef.current && actionId === latestActionIdRef.current) {
            setError(message)
        }
    }, [])

    const updateCarrier = async (carrierCode, data) => {
        const actionId = beginLoading()

        try {
            const response = await api.put(`/admin/carriers/${carrierCode}`, data)
            return { success: true, data: response.data }
        } catch (err) {
            const errorMessage = err.response?.data?.message || 'Failed to update carrier'
            commitError(actionId, errorMessage)
            return { success: false, error: errorMessage }
        } finally {
            endLoading(actionId)
        }
    }

    const validateCredentials = async (carrierCode) => {
        const actionId = beginLoading()

        try {
            const response = await api.post(`/admin/carriers/${carrierCode}/validate`)
            return { success: true, data: response.data }
        } catch (err) {
            const errorMessage = err.response?.data?.message || 'Validation failed'
            commitError(actionId, errorMessage)
            return { success: false, error: errorMessage }
        } finally {
            endLoading(actionId)
        }
    }

    const toggleCarrier = async (carrierCode) => {
        const actionId = beginLoading()

        try {
            const response = await api.post(`/admin/carriers/${carrierCode}/toggle`)
            return { success: true, data: response.data }
        } catch (err) {
            const errorMessage = err.response?.data?.message || 'Failed to toggle carrier'
            commitError(actionId, errorMessage)
            return { success: false, error: errorMessage }
        } finally {
            endLoading(actionId)
        }
    }

    return {
        updateCarrier,
        validateCredentials,
        toggleCarrier,
        loading,
        error,
        clearError: () => {
            if (isMountedRef.current) {
                setError(null)
            }
        }
    }
}

/**
 * Hook for fetching shipping options (customer-facing shipping methods)
 */
export function useShippingOptions() {
    const [options, setOptions] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const requestIdRef = useRef(0)

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    const fetchOptions = useCallback(async () => {
        const requestId = ++requestIdRef.current
        setLoading(true)
        setError(null)

        try {
            const response = await api.get('/shippingmethods?activeOnly=false')

            if (requestId !== requestIdRef.current) {
                return
            }

            setOptions(response.data || [])
        } catch (err) {
            if (requestId !== requestIdRef.current) {
                return
            }

            console.error('Failed to fetch shipping options:', err)
            setError(err.response?.data?.message || 'Failed to load shipping options')
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }, [])

    useEffect(() => {
        fetchOptions()
    }, [fetchOptions])

    return {
        options,
        loading,
        error,
        refetch: fetchOptions
    }
}

/**
 * Hook for shipping option CRUD operations
 */
export function useShippingOptionActions() {
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState(null)
    const pendingRequestsRef = useRef(0)
    const latestActionIdRef = useRef(0)
    const isMountedRef = useRef(true)

    useEffect(() => {
        return () => {
            isMountedRef.current = false
            pendingRequestsRef.current = 0
            latestActionIdRef.current = 0
        }
    }, [])

    const beginLoading = useCallback(() => {
        const actionId = latestActionIdRef.current + 1
        latestActionIdRef.current = actionId
        pendingRequestsRef.current += 1

        if (isMountedRef.current) {
            setLoading(true)
            setError(null)
        }
        return actionId
    }, [])

    const endLoading = useCallback((actionId) => {
        pendingRequestsRef.current = Math.max(0, pendingRequestsRef.current - 1)

        if (isMountedRef.current && (actionId === latestActionIdRef.current || pendingRequestsRef.current === 0)) {
            setLoading(pendingRequestsRef.current > 0)
        }
    }, [])

    const commitError = useCallback((actionId, message) => {
        if (isMountedRef.current && actionId === latestActionIdRef.current) {
            setError(message)
        }
    }, [])

    const createOption = async (data) => {
        const actionId = beginLoading()

        try {
            const response = await api.post('/shippingmethods', data)
            return { success: true, data: response.data }
        } catch (err) {
            const errorMessage = err.response?.data?.message || 'Failed to create shipping option'
            commitError(actionId, errorMessage)
            return { success: false, error: errorMessage }
        } finally {
            endLoading(actionId)
        }
    }

    const updateOption = async (id, data) => {
        const actionId = beginLoading()

        try {
            const response = await api.put(`/shippingmethods/${id}`, data)
            return { success: true, data: response.data }
        } catch (err) {
            const errorMessage = err.response?.data?.message || 'Failed to update shipping option'
            commitError(actionId, errorMessage)
            return { success: false, error: errorMessage }
        } finally {
            endLoading(actionId)
        }
    }

    const deleteOption = async (id) => {
        const actionId = beginLoading()

        try {
            await api.delete(`/shippingmethods/${id}`)
            return { success: true }
        } catch (err) {
            const errorMessage = err.response?.data?.message || 'Failed to delete shipping option'
            commitError(actionId, errorMessage)
            return { success: false, error: errorMessage }
        } finally {
            endLoading(actionId)
        }
    }

    return {
        createOption,
        updateOption,
        deleteOption,
        loading,
        error,
        clearError: () => {
            if (isMountedRef.current) {
                setError(null)
            }
        }
    }
}

/**
 * Carrier display configuration
 * PHASE 1: 'manual' is a FIRST-CLASS carrier, not a fallback
 */
export const CARRIER_INFO = {
    manual: {
        name: 'Manual Fulfillment',
        logo: '🛠️',
        color: 'slate',
        bgClass: 'bg-slate-50',
        borderClass: 'border-slate-200',
        textClass: 'text-slate-700',
        region: 'Global'
    },
    bosta: {
        name: 'Bosta',
        logo: '🚛',
        color: 'emerald',
        bgClass: 'bg-emerald-50',
        borderClass: 'border-emerald-200',
        textClass: 'text-emerald-700',
        region: 'Egypt'
    },
    aramex: {
        name: 'Aramex',
        logo: '📦',
        color: 'red',
        bgClass: 'bg-red-50',
        borderClass: 'border-red-200',
        textClass: 'text-red-700',
        region: 'MENA'
    },
    mylerz: {
        name: 'Mylerz',
        logo: '🚚',
        color: 'blue',
        bgClass: 'bg-blue-50',
        borderClass: 'border-blue-200',
        textClass: 'text-blue-700',
        region: 'Egypt'
    },
    dhl: {
        name: 'DHL Express',
        logo: '✈️',
        color: 'yellow',
        bgClass: 'bg-yellow-50',
        borderClass: 'border-yellow-200',
        textClass: 'text-yellow-700',
        region: 'International'
    }
}

/**
 * Get carrier display info with fallback
 */
export function getCarrierInfo(carrierCode) {
    return CARRIER_INFO[carrierCode?.toLowerCase()] || {
        name: carrierCode || 'Unknown',
        logo: '📦',
        color: 'gray',
        bgClass: 'bg-gray-50',
        borderClass: 'border-gray-200',
        textClass: 'text-gray-700',
        region: 'Unknown'
    }
}

/**
 * Pricing strategy options
 */
export const PRICING_STRATEGIES = [
    { value: 'flat', label: 'Flat Rate', description: 'Fixed shipping cost' },
    { value: 'per_kg', label: 'Per KG', description: 'Base + weight-based' },
    { value: 'free_over', label: 'Free Over X', description: 'Free when order exceeds threshold' },
    { value: 'carrier_rates', label: 'Carrier Rates', description: 'Use carrier\'s live rates' }
]

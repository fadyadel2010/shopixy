/**
 * Shipping Admin API Hook
 * Phase: Shipping Frontend
 * 
 * Provides all API calls for shipping admin dashboard.
 * 
 * PURE CONSUMER - NO BUSINESS LOGIC
 * All status/lifecycle logic comes from backend.
 * 
 * APIs:
 * - GET /api/admin/shipments - List with filters
 * - GET /api/admin/shipments/:id - Single shipment details
 * - PUT /api/admin/shipments/:id/status - Update status
 * - POST /api/admin/shipments/:id/cancel - Cancel shipment
 * - GET /api/webhooks/shipping/health - Health check
 */

import { useState, useEffect, useCallback, useRef } from 'react'
import api from '../../../services/api'
import { requireShipmentActionAuthority } from '../shipmentActionAuthority'
import { requestShipmentCancellation } from '../cancellationOutcome'

/**
 * Hook for fetching paginated shipments list
 */
export function useShipmentsList(filters = {}) {
    const [shipments, setShipments] = useState([])
    const [pagination, setPagination] = useState({
        page: 1,
        pageSize: 20,
        totalCount: 0,
        totalPages: 0
    })
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const requestIdRef = useRef(0)

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    const fetchShipments = useCallback(async (pageNumber = 1) => {
        const requestId = ++requestIdRef.current
        setLoading(true)
        setError(null)

        try {
            const params = new URLSearchParams()
            params.append('page', pageNumber)
            params.append('pageSize', filters.pageSize || 20)

            if (filters.status) params.append('status', filters.status)
            if (filters.carrierCode) params.append('carrier', filters.carrierCode)
            if (filters.isCod !== undefined) params.append('isCod', filters.isCod)
            if (filters.dateFrom) params.append('fromDate', filters.dateFrom)
            if (filters.dateTo) params.append('toDate', filters.dateTo)
            if (filters.search) params.append('search', filters.search)

            const response = await api.get(`/admin/shipments?${params.toString()}`)

            if (requestId !== requestIdRef.current) {
                return
            }

            setShipments(response.data.items || [])
            setPagination({
                page: response.data.page || pageNumber,
                pageSize: response.data.pageSize || 20,
                totalCount: response.data.totalCount || 0,
                totalPages: response.data.totalPages || 0
            })
        } catch (err) {
            if (requestId !== requestIdRef.current) {
                return
            }

            console.error('Failed to fetch shipments:', err)
            setError(err.response?.data?.message || 'Failed to load shipments')
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }, [filters])

    useEffect(() => {
        fetchShipments(1)
    }, [filters.status, filters.carrierCode, filters.isCod, filters.dateFrom, filters.dateTo, filters.search, filters.pageSize])

    const goToPage = (page) => fetchShipments(page)
    const refetch = () => fetchShipments(pagination.page)

    return {
        shipments,
        pagination,
        loading,
        error,
        goToPage,
        refetch
    }
}

/**
 * Hook for fetching single shipment details
 */
export function useShipmentDetails(shipmentId) {
    const [shipment, setShipment] = useState(null)
    const [loading, setLoading] = useState(!!shipmentId) // Only start loading if we have an ID
    const [error, setError] = useState(null)
    const requestIdRef = useRef(0)

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    const fetchShipment = useCallback(async () => {
        // Guard: Don't fetch if no valid shipmentId
        if (!shipmentId || shipmentId === 'undefined') {
            requestIdRef.current += 1
            setShipment(null)
            setError(null)
            setLoading(false)
            return
        }

        const requestId = ++requestIdRef.current
        setLoading(true)
        setError(null)

        try {
            const response = await api.get(`/admin/shipments/${shipmentId}`)

            if (requestId !== requestIdRef.current) {
                return
            }

            setShipment(response.data)
        } catch (err) {
            if (requestId !== requestIdRef.current) {
                return
            }

            console.error('Failed to fetch shipment:', err)
            setError(err.response?.data?.message || 'Failed to load shipment')
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }, [shipmentId])

    useEffect(() => {
        if (shipmentId && shipmentId !== 'undefined') {
            fetchShipment()
        } else {
            requestIdRef.current += 1
            setShipment(null)
            setError(null)
            setLoading(false)
        }
    }, [shipmentId, fetchShipment])

    return {
        shipment,
        loading,
        error,
        refetch: fetchShipment
    }
}

/**
 * Hook for shipment admin actions
 */
export function useShipmentActions() {
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState(null)
    const pendingRequestsRef = useRef(0)
    const lastActionIdRef = useRef(0)
    const isMountedRef = useRef(true)

    useEffect(() => {
        isMountedRef.current = true
        return () => {
            isMountedRef.current = false
            pendingRequestsRef.current = 0
            lastActionIdRef.current = 0
        }
    }, [])

    const beginLoading = useCallback(() => {
        const actionId = lastActionIdRef.current + 1
        lastActionIdRef.current = actionId
        pendingRequestsRef.current += 1

        if (isMountedRef.current) {
            setLoading(true)
            setError(null)
        }
        return actionId
    }, [])

    const endLoading = useCallback(() => {
        pendingRequestsRef.current = Math.max(0, pendingRequestsRef.current - 1)

        if (isMountedRef.current && pendingRequestsRef.current === 0) {
            setLoading(false)
        }
    }, [])

    const commitError = useCallback((actionId, message) => {
        if (isMountedRef.current && actionId === lastActionIdRef.current) {
            setError(message)
        }
    }, [])

    const updateStatus = async (shipmentId, newStatus, notes = '') => {
        void shipmentId
        void newStatus
        void notes
        // Generic client-selected status is deliberately not an execution path.
        // A future action must first be represented by a current backend-issued
        // fulfillment action for the exact order target.
        return { success: false, error: 'Shipment status transitions require current backend fulfillment action authority.' }
    }

    const cancelShipment = async (shipmentId, reason = '') => {
        const actionId = beginLoading()

        try {
            const data = await requestShipmentCancellation(api, shipmentId, reason)
            return { success: data.outcome === 'Confirmed', data }
        } catch (err) {
            const errorMessage = err.response?.data?.message || 'Failed to cancel shipment'
            commitError(actionId, errorMessage)
            return { success: false, error: errorMessage }
        } finally {
            endLoading()
        }
    }

    const clearError = useCallback(() => {
        // A target change/dismissal withdraws visibility of older errors,
        // without retrying or altering the already issued backend command.
        lastActionIdRef.current += 1
        if (isMountedRef.current) {
            setError(null)
        }
    }, [])
    return {
        updateStatus,
        cancelShipment,
        loading,
        error,
        clearError,
    }
}

export function useShipmentActionAuthority(orderId) {
    const [authority, setAuthority] = useState(null)
    const [loading, setLoading] = useState(Boolean(orderId))
    const [error, setError] = useState(null)
    const requestIdRef = useRef(0)

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    const fetchAuthority = useCallback(async () => {
        if (!orderId || orderId === 'undefined') {
            requestIdRef.current += 1
            setAuthority(null)
            setError(null)
            setLoading(false)
            return
        }

        const requestId = ++requestIdRef.current
        setLoading(true)
        setError(null)

        try {
            const response = await api.get(`/orders/${orderId}/fulfillment/actions`)

            if (requestId !== requestIdRef.current) {
                return
            }

            const currentAuthority = requireShipmentActionAuthority(response.data, orderId)
            setAuthority(currentAuthority)
            return currentAuthority
        } catch (err) {
            if (requestId !== requestIdRef.current) {
                return
            }

            setAuthority(null)
            setError(err.response?.data?.message || err.message || 'Failed to load shipment action authority')
            throw err
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }, [orderId])

    useEffect(() => {
        fetchAuthority().catch(() => {})
    }, [fetchAuthority])

    return {
        authority,
        loading,
        error,
        refetch: fetchAuthority
    }
}

/**
 * Hook for shipping health status
 */
export function useShippingHealth() {
    const [health, setHealth] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const requestIdRef = useRef(0)

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    const fetchHealth = useCallback(async () => {
        const requestId = ++requestIdRef.current
        setLoading(true)
        setError(null)

        try {
            const response = await api.get('/webhooks/shipping/health')

            if (requestId !== requestIdRef.current) {
                return
            }

            setHealth(response.data)
        } catch (err) {
            if (requestId !== requestIdRef.current) {
                return
            }

            console.error('Failed to fetch shipping health:', err)
            setError(err.response?.data?.message || 'Failed to load health status')
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }, [])

    useEffect(() => {
        fetchHealth()
    }, [fetchHealth])

    return {
        health,
        loading,
        error,
        refetch: fetchHealth
    }
}

/**
 * Shipment status configuration
 * Colors and labels for each canonical status
 * 
 * NOTE: These are display-only - NO lifecycle logic
 */
export const SHIPMENT_STATUS_CONFIG = {
    PendingExternalRegistration: {
        bgColor: 'bg-amber-100', textColor: 'text-amber-800', borderColor: 'border-amber-300', icon: '⏳', order: -1
    },
    Pending: {
        color: 'gray',
        bgColor: 'bg-gray-100',
        textColor: 'text-gray-700',
        borderColor: 'border-gray-300',
        icon: '⏳',
        order: 0
    },
    ReadyForPickup: {
        color: 'blue',
        bgColor: 'bg-blue-100',
        textColor: 'text-blue-700',
        borderColor: 'border-blue-300',
        icon: '📦',
        order: 1
    },
    PickedUp: {
        color: 'indigo',
        bgColor: 'bg-indigo-100',
        textColor: 'text-indigo-700',
        borderColor: 'border-indigo-300',
        icon: '🚛',
        order: 2
    },
    InTransit: {
        color: 'sky',
        bgColor: 'bg-sky-100',
        textColor: 'text-sky-700',
        borderColor: 'border-sky-300',
        icon: '🚚',
        order: 3
    },
    OutForDelivery: {
        color: 'amber',
        bgColor: 'bg-amber-100',
        textColor: 'text-amber-700',
        borderColor: 'border-amber-300',
        icon: '🏃',
        order: 4
    },
    Delivered: {
        color: 'green',
        bgColor: 'bg-green-100',
        textColor: 'text-green-700',
        borderColor: 'border-green-300',
        icon: '✅',
        order: 5,
        isTerminal: true
    },
    Failed: {
        color: 'red',
        bgColor: 'bg-red-100',
        textColor: 'text-red-700',
        borderColor: 'border-red-300',
        icon: '❌',
        order: 100,
        isTerminal: true
    },
    Cancelled: {
        color: 'rose',
        bgColor: 'bg-rose-100',
        textColor: 'text-rose-700',
        borderColor: 'border-rose-300',
        icon: '🚫',
        order: 100,
        isTerminal: true
    },
    Returned: {
        color: 'orange',
        bgColor: 'bg-orange-100',
        textColor: 'text-orange-700',
        borderColor: 'border-orange-300',
        icon: '↩️',
        order: 100,
        isTerminal: true
    }
}

/**
 * Get display config for a status
 */
export function getStatusConfig(status) {
    return SHIPMENT_STATUS_CONFIG[status] || SHIPMENT_STATUS_CONFIG.Pending
}

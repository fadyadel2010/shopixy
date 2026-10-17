/**
 * WhatsApp Analytics Hook
 * Phase 14: Analytics Dashboard
 * 
 * Provides data fetching for:
 * - KPI Cards
 * - Delivery Funnel
 * - Message Logs (Paginated)
 */

import { useState, useCallback, useEffect, useRef } from 'react'
import api from '../../../services/api'

export function useWhatsAppAnalytics() {
    const [loading, setLoading] = useState(false)
    const [analyticsError, setAnalyticsError] = useState(null)
    const [logsError, setLogsError] = useState(null)
    const pendingRequestsRef = useRef(0)
    const latestAnalyticsRequestIdRef = useRef(0)
    const latestLogsRequestIdRef = useRef(0)
    const isMountedRef = useRef(true)

    useEffect(() => {
        isMountedRef.current = true
        return () => {
            isMountedRef.current = false
            pendingRequestsRef.current = 0
            latestAnalyticsRequestIdRef.current = 0
            latestLogsRequestIdRef.current = 0
        }
    }, [])

    const beginAnalyticsLoading = useCallback(() => {
        pendingRequestsRef.current += 1
        const requestId = latestAnalyticsRequestIdRef.current + 1
        latestAnalyticsRequestIdRef.current = requestId

        if (isMountedRef.current) {
            setAnalyticsError(null)
            setLoading(true)
        }

        return requestId
    }, [])

    const beginLogsLoading = useCallback(() => {
        pendingRequestsRef.current += 1
        const requestId = latestLogsRequestIdRef.current + 1
        latestLogsRequestIdRef.current = requestId

        if (isMountedRef.current) {
            setLogsError(null)
            setLoading(true)
        }

        return requestId
    }, [])

    const endLoading = useCallback(() => {
        pendingRequestsRef.current = Math.max(0, pendingRequestsRef.current - 1)

        if (isMountedRef.current && pendingRequestsRef.current === 0) {
            setLoading(false)
        }
    }, [])

    const commitAnalyticsError = useCallback((message, requestId) => {
        if (isMountedRef.current && requestId === latestAnalyticsRequestIdRef.current) {
            setAnalyticsError(message)
        }
    }, [])

    const commitLogsError = useCallback((message, requestId) => {
        if (isMountedRef.current && requestId === latestLogsRequestIdRef.current) {
            setLogsError(message)
        }
    }, [])

    const getAnalytics = useCallback(async (dateFrom, dateTo) => {
        const requestId = beginAnalyticsLoading()
        try {
            const params = new URLSearchParams()
            if (dateFrom) params.append('from', dateFrom)
            if (dateTo) params.append('to', dateTo)

            const response = await api.get(`/admin/whatsapp/analytics?${params.toString()}`)
            return {
                sentMessages: response.data.totalMessagesSent,
                failedMessages: response.data.totalMessagesFailed,
                successRate: response.data.successRate,
                messagesByType: response.data.messagesByType,
            }
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to fetch analytics'
            commitAnalyticsError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginAnalyticsLoading, commitAnalyticsError, endLoading])

    const getLogs = useCallback(async (filters = {}, page = 1, pageSize = 20) => {
        const requestId = beginLogsLoading()
        try {
            const params = new URLSearchParams()
            params.append('page', page)
            params.append('pageSize', pageSize)

            if (filters.status) params.append('status', filters.status.toUpperCase())
            if (filters.type) params.append('messageType', filters.type.toLowerCase() === 'orderconfirmation' ? 'ORDER_CONFIRMATION' : filters.type.toUpperCase())
            if (filters.phone) params.append('phone', filters.phone)
            if (filters.dateFrom) params.append('from', filters.dateFrom)
            if (filters.dateTo) params.append('to', filters.dateTo)

            const response = await api.get(`/admin/whatsapp/logs?${params.toString()}`)
            const data = response.data
            return {
                ...data,
                totalPages: Math.ceil(data.totalCount / (data.pageSize || pageSize)),
                items: data.items.map(item => ({
                    ...item,
                    phoneNumber: item.targetPhone,
                    type: ({ OTP: 'Otp', ORDERCONFIRMATION: 'OrderConfirmation' })[item.messageType?.replaceAll('_', '').toUpperCase()] || item.messageType,
                    status: ({ SENT: 'Sent', FAILED: 'Failed', DELIVERED: 'Delivered' })[item.status?.toUpperCase()] || item.status,
                    errorMessage: item.failureReason,
                })),
            }
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to fetch message logs'
            commitLogsError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLogsLoading, commitLogsError, endLoading])

    return {
        loading,
        analyticsError,
        logsError,
        error: analyticsError || logsError, // Combined error for backward compatibility
        getAnalytics,
        getLogs
    }
}

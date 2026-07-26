/**
 * WhatsApp Admin API Hook
 * Phase 14: WhatsApp Admin UI
 * 
 * Provides all API calls for WhatsApp administration.
 * 
 * APIs:
 * - Accounts Management (List, Get, Create, Update, Enable/Disable)
 * - Template Management (CRUD)
 * - Safety Controls (Kill Switch, QR Code)
 */

import { useState, useCallback, useEffect, useRef } from 'react'
import api from '../../../services/api'

export function useWhatsAppApi() {
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState(null)
    const pendingRequestsRef = useRef(0)
    const latestRequestIdRef = useRef(0)
    const isMountedRef = useRef(true)

    useEffect(() => {
        return () => {
            isMountedRef.current = false
            pendingRequestsRef.current = 0
            latestRequestIdRef.current = 0
        }
    }, [])

    const beginLoading = useCallback(() => {
        pendingRequestsRef.current += 1
        const requestId = latestRequestIdRef.current + 1
        latestRequestIdRef.current = requestId

        if (isMountedRef.current) {
            setError(null)
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

    const commitError = useCallback((message, requestId) => {
        if (isMountedRef.current && requestId === latestRequestIdRef.current) {
            setError(message)
        }
    }, [])

    // ==================================================================================
    // Accounts
    // ==================================================================================

    const getAccounts = useCallback(async () => {
        const requestId = beginLoading()
        try {
            const response = await api.get('/admin/whatsapp/accounts')
            return response.data
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to fetch accounts'
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    const getAccount = useCallback(async (id) => {
        const requestId = beginLoading()
        try {
            const response = await api.get(`/admin/whatsapp/accounts/${id}`)
            return response.data
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to fetch account'
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    const createAccount = useCallback(async (data) => {
        const requestId = beginLoading()
        try {
            const response = await api.post('/admin/whatsapp/accounts', data)
            return response.data
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to create account'
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    const getAccountStatus = useCallback(async (id) => {
        try {
            const response = await api.get(`/admin/whatsapp/accounts/${id}/status`)
            return response.data
        } catch (err) {
            console.error('Failed to check account status:', err)
            return null
        }
    }, [])

    const getQrCode = useCallback(async (id) => {
        const requestId = beginLoading()
        try {
            const response = await api.get(`/admin/whatsapp/accounts/${id}/qr`)
            return response.data
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to generate QR code'
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    const toggleAccount = useCallback(async (id, enable) => {
        const requestId = beginLoading()
        try {
            const endpoint = enable ? 'enable' : 'disable'
            await api.post(`/admin/whatsapp/accounts/${id}/${endpoint}`)
            return true
        } catch (err) {
            const msg = err.response?.data?.message || `Failed to ${enable ? 'enable' : 'disable'} account`
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    const disconnectAccount = useCallback(async (id) => {
        const requestId = beginLoading()
        try {
            await api.post(`/admin/whatsapp/accounts/${id}/disconnect`)
            return true
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to disconnect account'
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    const deleteAccount = useCallback(async (id) => {
        const requestId = beginLoading()
        try {
            await api.delete(`/admin/whatsapp/accounts/${id}`)
            return true
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to delete account'
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    const markAsConnected = useCallback(async (id) => {
        const requestId = beginLoading()
        try {
            await api.post(`/admin/whatsapp/accounts/${id}/mark-connected`)
            return true
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to mark account as connected'
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    // ==================================================================================
    // Templates
    // ==================================================================================

    const getTemplates = useCallback(async () => {
        const requestId = beginLoading()
        try {
            const response = await api.get('/admin/whatsapp/templates')
            return response.data
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to fetch templates'
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    const createTemplate = useCallback(async (data) => {
        const requestId = beginLoading()
        try {
            const response = await api.post('/admin/whatsapp/templates', data)
            return response.data
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to create template'
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    const updateTemplate = useCallback(async (id, data) => {
        const requestId = beginLoading()
        try {
            const response = await api.put(`/admin/whatsapp/templates/${id}`, data)
            return response.data
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to update template'
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    const deleteTemplate = useCallback(async (id) => {
        const requestId = beginLoading()
        try {
            await api.delete(`/admin/whatsapp/templates/${id}`)
            return true
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to delete template'
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    // ==================================================================================
    // Safety (Kill Switch)
    // ==================================================================================

    const getKillSwitchStatus = useCallback(async () => {
        try {
            const response = await api.get('/admin/whatsapp/killswitch/status')
            return response.data
        } catch (err) {
            console.error('Failed to get kill switch status:', err)
            return { isKilled: false }
        }
    }, [])

    const activateKillSwitch = useCallback(async (reason) => {
        const requestId = beginLoading()
        try {
            await api.post('/admin/whatsapp/killswitch/activate', { reason })
            return true
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to activate kill switch'
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    const deactivateKillSwitch = useCallback(async () => {
        const requestId = beginLoading()
        try {
            await api.post('/admin/whatsapp/killswitch/deactivate')
            return true
        } catch (err) {
            const msg = err.response?.data?.message || 'Failed to deactivate kill switch'
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading])

    return {
        loading,
        error,
        clearError: () => {
            if (isMountedRef.current) {
                setError(null)
            }
        },
        // Accounts
        getAccounts,
        getAccount,
        createAccount,
        getAccountStatus,
        getQrCode,
        toggleAccount,
        disconnectAccount,
        deleteAccount,
        markAsConnected,
        // Templates
        getTemplates,
        createTemplate,
        updateTemplate,
        deleteTemplate,
        // Safety
        getKillSwitchStatus,
        activateKillSwitch,
        deactivateKillSwitch
    }
}

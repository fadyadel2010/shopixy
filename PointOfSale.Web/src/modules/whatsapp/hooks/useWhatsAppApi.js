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
import { useTranslation } from 'react-i18next'
import api from '../../../services/api'

export function useWhatsAppApi() {
    const { t } = useTranslation()
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState(null)
    const pendingRequestsRef = useRef(0)
    const latestRequestIdRef = useRef(0)
    const isMountedRef = useRef(true)

    useEffect(() => {
        isMountedRef.current = true
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

    const getErrorMessage = useCallback((err, fallbackKey) => {
        const payload = err.response?.data
        const fallback = t(fallbackKey)
        const validationErrors = payload?.data?.errors || payload?.details?.errors || payload?.errors
        const validationError = validationErrors && typeof validationErrors === 'object'
            ? Object.values(validationErrors).flat().find(Boolean)
            : null

        if (payload?.code) {
            return t(`whatsapp.errors.${payload.code}`, {
                defaultValue: validationError || payload.message || fallback
            })
        }

        return validationError || payload?.message || fallback
    }, [t])

    // ==================================================================================
    // Accounts
    // ==================================================================================

    const getAccounts = useCallback(async () => {
        const requestId = beginLoading()
        try {
            const response = await api.get('/admin/whatsapp/accounts')
            return response.data
        } catch (err) {
            const msg = getErrorMessage(err, 'whatsapp.errors.fetchAccounts')
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading, getErrorMessage])

    const getAccount = useCallback(async (id) => {
        const requestId = beginLoading()
        try {
            const response = await api.get(`/admin/whatsapp/accounts/${id}`)
            return response.data
        } catch (err) {
            const msg = getErrorMessage(err, 'whatsapp.errors.fetchAccount')
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading, getErrorMessage])

    const createAccount = useCallback(async (data) => {
        const requestId = beginLoading()
        try {
            const response = await api.post('/admin/whatsapp/accounts', data)
            return response.data
        } catch (err) {
            const msg = getErrorMessage(err, 'whatsapp.errors.createAccount')
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading, getErrorMessage])

    const updateAccount = useCallback(async (id, data) => {
        const requestId = beginLoading()
        try {
            const response = await api.put(`/admin/whatsapp/accounts/${id}`, data)
            return response.data
        } catch (err) {
            const msg = getErrorMessage(err, 'whatsapp.errors.updateAccount')
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading, getErrorMessage])

    const getAccountStatus = useCallback(async (id) => {
        try {
            const response = await api.get(`/admin/whatsapp/accounts/${id}/status`)
            return response.data
        } catch (err) {
            const msg = getErrorMessage(err, 'whatsapp.errors.fetchAccountStatus')
            throw new Error(msg)
        }
    }, [getErrorMessage])

    const getQrCode = useCallback(async (id) => {
        const requestId = beginLoading()
        try {
            const response = await api.get(`/admin/whatsapp/accounts/${id}/qr`)
            return response.data
        } catch (err) {
            const msg = getErrorMessage(err, 'whatsapp.errors.generateQr')
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading, getErrorMessage])

    const toggleAccount = useCallback(async (id, enable) => {
        const requestId = beginLoading()
        try {
            const endpoint = enable ? 'enable' : 'disable'
            await api.post(`/admin/whatsapp/accounts/${id}/${endpoint}`)
            return true
        } catch (err) {
            const msg = getErrorMessage(err, enable ? 'whatsapp.errors.enableAccount' : 'whatsapp.errors.disableAccount')
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading, getErrorMessage])

    const disconnectAccount = useCallback(async (id) => {
        const requestId = beginLoading()
        try {
            await api.post(`/admin/whatsapp/accounts/${id}/disconnect`)
            return true
        } catch (err) {
            const msg = getErrorMessage(err, 'whatsapp.errors.disconnectAccount')
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading, getErrorMessage])

    const deleteAccount = useCallback(async (id) => {
        const requestId = beginLoading()
        try {
            await api.delete(`/admin/whatsapp/accounts/${id}`)
            return true
        } catch (err) {
            const msg = getErrorMessage(err, 'whatsapp.errors.deleteAccount')
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading, getErrorMessage])

    // ==================================================================================
    // Templates
    // ==================================================================================

    const getTemplates = useCallback(async () => {
        const requestId = beginLoading()
        try {
            const response = await api.get('/admin/whatsapp/templates')
            return response.data
        } catch (err) {
            const msg = getErrorMessage(err, 'whatsapp.errors.fetchTemplates')
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading, getErrorMessage])

    const createTemplate = useCallback(async (data) => {
        const requestId = beginLoading()
        try {
            const response = await api.post('/admin/whatsapp/templates', data)
            return response.data
        } catch (err) {
            const msg = getErrorMessage(err, 'whatsapp.errors.createTemplate')
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading, getErrorMessage])

    const updateTemplate = useCallback(async (id, data) => {
        const requestId = beginLoading()
        try {
            const response = await api.put(`/admin/whatsapp/templates/${id}`, data)
            return response.data
        } catch (err) {
            const msg = getErrorMessage(err, 'whatsapp.errors.updateTemplate')
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading, getErrorMessage])

    const deleteTemplate = useCallback(async (id) => {
        const requestId = beginLoading()
        try {
            await api.delete(`/admin/whatsapp/templates/${id}`)
            return true
        } catch (err) {
            const msg = getErrorMessage(err, 'whatsapp.errors.deleteTemplate')
            commitError(msg, requestId)
            throw new Error(msg)
        } finally {
            endLoading()
        }
    }, [beginLoading, commitError, endLoading, getErrorMessage])

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
        updateAccount,
        getAccountStatus,
        getQrCode,
        toggleAccount,
        disconnectAccount,
        deleteAccount,
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

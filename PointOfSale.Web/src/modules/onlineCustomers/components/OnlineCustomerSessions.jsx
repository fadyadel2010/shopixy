/**
 * Online Customer Sessions Panel Component
 * Phase P1: POS Admin - Online Customers Management
 */

import { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import {
    RefreshCw,
    LogOut,
    AlertTriangle,
    Smartphone,
    Monitor,
    Clock,
    MapPin,
    CheckCircle,
    XCircle,
    Loader2
} from 'lucide-react'
import { onlineCustomersApi } from '../services'
import { ConfirmActionModal } from './ConfirmActionModal'
import { useToast } from '../../../components/Toast'
import { formatDistanceToNow, format } from 'date-fns'
import { ar, enUS } from 'date-fns/locale'

export function OnlineCustomerSessions({ customerId, onSessionsChange }) {
    const { t, i18n } = useTranslation()
    const [sessions, setSessions] = useState([])
    const [sessionsOwner, setSessionsOwner] = useState({ customerId: null, requestId: 0 })
    const [loading, setLoading] = useState(true)
    const [includeRevoked, setIncludeRevoked] = useState(true)
    const [revokeModal, setRevokeModal] = useState(null) // { type: 'single' | 'all', sessionId? }
    const [revokeLoading, setRevokeLoading] = useState(false)
    const requestIdRef = useRef(0)
    const customerIdRef = useRef(customerId)

    customerIdRef.current = customerId

    const isSameCustomerId = (left, right) => String(left ?? '').toLowerCase() === String(right ?? '').toLowerCase()

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    const { showSuccess, showError } = useToast()

    const getDateLocale = () => i18n.language.startsWith('ar') ? ar : enUS

    useEffect(() => {
        const requestId = ++requestIdRef.current

        setSessions([])
        setSessionsOwner({ customerId: customerId ?? null, requestId })

        if (!customerId) {
            setLoading(false)
            return
        }

        setLoading(true)
        void fetchSessions(customerId, includeRevoked, requestId)
    }, [customerId, includeRevoked])

    useEffect(() => {
        setRevokeModal(null)
    }, [customerId])

    const fetchSessions = async (targetCustomerId, includeRevokedValue, requestId = null) => {
        const resolvedCustomerId = targetCustomerId ?? customerId
        const resolvedIncludeRevoked = includeRevokedValue ?? includeRevoked
        const matchesCurrentCustomer = isSameCustomerId(customerIdRef.current, resolvedCustomerId)

        if (!matchesCurrentCustomer) {
            return
        }

        const activeRequestId = requestId ?? ++requestIdRef.current
        const isCurrentRequest =
            activeRequestId === requestIdRef.current
            && isSameCustomerId(customerIdRef.current, resolvedCustomerId)

        if (!resolvedCustomerId) {
            if (isCurrentRequest) {
                setSessions([])
                setSessionsOwner({ customerId: null, requestId: activeRequestId })
                setLoading(false)
            }
            return
        }

        if (!isCurrentRequest) {
            return
        }

        setSessionsOwner({ customerId: resolvedCustomerId, requestId: activeRequestId })
        setLoading(true)
        try {
            const data = await onlineCustomersApi.getCustomerSessions(resolvedCustomerId, resolvedIncludeRevoked)

            if (activeRequestId !== requestIdRef.current || !isSameCustomerId(customerIdRef.current, resolvedCustomerId)) {
                return
            }

            setSessions(data.sessions || [])
            setSessionsOwner({ customerId: resolvedCustomerId, requestId: activeRequestId })
        } catch (error) {
            if (activeRequestId !== requestIdRef.current || !isSameCustomerId(customerIdRef.current, resolvedCustomerId)) {
                return
            }

            console.error('Failed to fetch sessions:', error)
            showError(t('common.error'))
        } finally {
            if (activeRequestId === requestIdRef.current && isSameCustomerId(customerIdRef.current, resolvedCustomerId)) {
                setLoading(false)
            }
        }
    }

    const handleRevokeSession = (sessionId) => {
        setRevokeModal({ type: 'single', sessionId })
    }

    const handleRevokeAll = () => {
        setRevokeModal({ type: 'all' })
    }

    const confirmRevoke = async (reason) => {
        setRevokeLoading(true)
        try {
            if (revokeModal.type === 'single') {
                await onlineCustomersApi.revokeSession(revokeModal.sessionId, reason)
                showSuccess(t('common.success'))
            } else {
                const result = await onlineCustomersApi.revokeAllSessions(customerId, reason)
                showSuccess(`${result.affectedCount} ${t('common.success')}`)
            }
            setRevokeModal(null)

            if (isSameCustomerId(customerIdRef.current, customerId)) {
                await fetchSessions(customerId, includeRevoked)
            }

            onSessionsChange?.()
        } catch (error) {
            console.error('Failed to revoke session:', error)
            showError(error.response?.data?.message || t('common.error'))
        } finally {
            setRevokeLoading(false)
        }
    }

    const getDeviceIcon = (userAgent) => {
        if (!userAgent) return <Smartphone className="w-4 h-4" />
        const ua = userAgent.toLowerCase()
        if (ua.includes('mobile') || ua.includes('android') || ua.includes('ios') || ua.includes('iphone')) {
            return <Smartphone className="w-4 h-4" />
        }
        return <Monitor className="w-4 h-4" />
    }

    const getSessionStatus = (session) => {
        if (session.isSuspicious) {
            return {
                label: t('onlineCustomers.sessions.status.suspicious'),
                color: 'text-red-600 bg-red-100',
                icon: <AlertTriangle className="w-3.5 h-3.5" />
            }
        }
        if (session.isRevoked) {
            return {
                label: t('onlineCustomers.sessions.status.revoked'),
                color: 'text-gray-600 bg-gray-100',
                icon: <XCircle className="w-3.5 h-3.5" />
            }
        }
        if (session.isExpired) {
            return {
                label: t('onlineCustomers.sessions.status.expired'),
                color: 'text-amber-600 bg-amber-100',
                icon: <Clock className="w-3.5 h-3.5" />
            }
        }
        return {
            label: t('onlineCustomers.sessions.status.active'),
            color: 'text-green-600 bg-green-100',
            icon: <CheckCircle className="w-3.5 h-3.5" />
        }
    }

    const sessionsAreCurrent = Boolean(
        customerId
        && isSameCustomerId(sessionsOwner.customerId, customerId)
        && sessionsOwner.requestId === requestIdRef.current
    )
    const visibleSessions = sessionsAreCurrent ? sessions : []
    const activeSessions = visibleSessions.filter(s => !s.isRevoked && !s.isExpired)

    if (customerId && (loading || !sessionsAreCurrent) && visibleSessions.length === 0) {
        return (
            <div className="flex items-center justify-center py-8">
                <Loader2 className="w-6 h-6 animate-spin text-primary-600" />
                <span className="ml-2 text-gray-500">{t('common.loading')}</span>
            </div>
        )
    }

    return (
        <div className="space-y-4">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-2">
                    <h3 className="text-sm font-semibold text-gray-900">
                        {t('onlineCustomers.sessions.title')} ({activeSessions.length})
                    </h3>
                    <label className="flex items-center gap-2 text-xs text-gray-500">
                        <input
                            type="checkbox"
                            checked={includeRevoked}
                            onChange={(e) => setIncludeRevoked(e.target.checked)}
                            className="rounded border-gray-300"
                        />
                        {t('onlineCustomers.sessions.showRevoked')}
                    </label>
                </div>
                <div className="flex items-center gap-2">
                    <button
                        onClick={() => void fetchSessions(customerId, includeRevoked)}
                        className="p-1.5 text-gray-400 hover:text-gray-600 rounded"
                        title={t('onlineCustomers.sessions.refresh')}
                    >
                        <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
                    </button>
                    {activeSessions.length > 0 && (
                        <button
                            onClick={handleRevokeAll}
                            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-red-600 bg-red-50 hover:bg-red-100 rounded-lg transition-colors"
                        >
                            <LogOut className="w-3.5 h-3.5" />
                            {t('onlineCustomers.sessions.revokeAll')}
                        </button>
                    )}
                </div>
            </div>

            {/* Sessions List */}
            {visibleSessions.length === 0 ? (
                <div className="text-center py-8 text-gray-500">
                    <Smartphone className="w-8 h-8 mx-auto mb-2 text-gray-300" />
                    <p>{t('onlineCustomers.sessions.noSessions')}</p>
                </div>
            ) : (
                <div className="space-y-2">
                    {visibleSessions.map((session) => {
                        const status = getSessionStatus(session)
                        const isActive = !session.isRevoked && !session.isExpired

                        return (
                            <div
                                key={session.sessionId}
                                className={`p-3 rounded-lg border ${session.isSuspicious
                                    ? 'border-red-200 bg-red-50'
                                    : isActive
                                        ? 'border-gray-200 bg-white'
                                        : 'border-gray-100 bg-gray-50'
                                    }`}
                            >
                                <div className="flex items-start justify-between gap-3">
                                    {/* Device & Agent */}
                                    <div className="flex items-start gap-3 flex-1 min-w-0">
                                        <div className={`p-2 rounded-lg ${isActive ? 'bg-primary-100 text-primary-600' : 'bg-gray-100 text-gray-400'}`}>
                                            {getDeviceIcon(session.userAgent)}
                                        </div>
                                        <div className="flex-1 min-w-0">
                                            <div className="flex items-center gap-2">
                                                <span className="text-sm font-medium text-gray-900 truncate">
                                                    {session.userAgent || 'Unknown Device'}
                                                </span>
                                                <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full ${status.color}`}>
                                                    {status.icon}
                                                    {status.label}
                                                </span>
                                            </div>
                                            <div className="mt-1 flex items-center gap-3 text-xs text-gray-500">
                                                <span className="flex items-center gap-1">
                                                    <MapPin className="w-3 h-3" />
                                                    {session.ip || 'Unknown IP'}
                                                </span>
                                                <span className="flex items-center gap-1">
                                                    <Clock className="w-3 h-3" />
                                                    {formatDistanceToNow(new Date(session.createdAtUtc), { addSuffix: true, locale: getDateLocale() })}
                                                </span>
                                            </div>
                                            {session.isSuspicious && session.suspiciousReason && (
                                                <div className="mt-2 text-xs text-red-600 flex items-start gap-1">
                                                    <AlertTriangle className="w-3 h-3 mt-0.5 flex-shrink-0" />
                                                    {session.suspiciousReason}
                                                </div>
                                            )}
                                            {session.isRevoked && session.revokedReason && (
                                                <div className="mt-2 text-xs text-gray-500">
                                                    {t('onlineCustomers.sessions.status.revoked')}: {session.revokedReason}
                                                </div>
                                            )}
                                        </div>
                                    </div>

                                    {/* Actions */}
                                    {isActive && (
                                        <button
                                            onClick={() => handleRevokeSession(session.sessionId)}
                                            className="flex-shrink-0 p-1.5 text-red-500 hover:text-red-700 hover:bg-red-50 rounded transition-colors"
                                            title={t('onlineCustomers.sessions.revokeSession')}
                                        >
                                            <LogOut className="w-4 h-4" />
                                        </button>
                                    )}
                                </div>

                                {/* Session Details */}
                                <div className="mt-2 pt-2 border-t border-gray-100 grid grid-cols-2 gap-2 text-xs text-gray-500">
                                    <div>
                                        <span className="text-gray-400">{t('onlineCustomers.sessions.created')}: </span>
                                        {format(new Date(session.createdAtUtc), 'MMM d, yyyy h:mm a', { locale: getDateLocale() })}
                                    </div>
                                    <div>
                                        <span className="text-gray-400">{t('onlineCustomers.sessions.expires')}: </span>
                                        {format(new Date(session.expiresAtUtc), 'MMM d, yyyy h:mm a', { locale: getDateLocale() })}
                                    </div>
                                    {session.revokedAtUtc && (
                                        <div className="col-span-2">
                                            <span className="text-gray-400">{t('onlineCustomers.sessions.status.revoked')}: </span>
                                            {format(new Date(session.revokedAtUtc), 'MMM d, yyyy h:mm a', { locale: getDateLocale() })}
                                        </div>
                                    )}
                                </div>
                            </div>
                        )
                    })}
                </div>
            )}

            {/* Revoke Confirmation Modal */}
            <ConfirmActionModal
                isOpen={!!revokeModal}
                onClose={() => setRevokeModal(null)}
                onConfirm={confirmRevoke}
                title={revokeModal?.type === 'all' ? t('onlineCustomers.sessions.revokeAllConfirm') : t('onlineCustomers.sessions.revokeSessionConfirm')}
                message={
                    revokeModal?.type === 'all'
                        ? t('onlineCustomers.sessions.revokeAllMessage')
                        : t('onlineCustomers.sessions.revokeSessionMessage')
                }
                confirmLabel={revokeModal?.type === 'all' ? t('onlineCustomers.sessions.revokeAll') : t('onlineCustomers.sessions.revokeSession')}
                variant="danger"
                requireReason={true}
                reasonLabel={t('onlineCustomers.sessions.reasonLabel')}
                reasonPlaceholder={t('onlineCustomers.sessions.reasonPlaceholder')}
                loading={revokeLoading}
            />
        </div>
    )
}

export default OnlineCustomerSessions

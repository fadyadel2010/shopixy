import { useState, useEffect, useRef, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useRTL } from '../hooks/useRTL'
import api from '../services/api'
import { useAuthStore } from '../store/authStore'
import { getScopedStorageKey, safeReadJson, safeWriteJson } from '../utils/storageScope'
import { isServiceMarketplaceWorkflowActor } from '../marketplace/authority'

const SEEN_REQUESTS_KEY_PREFIX = 'marketplace_seen_request_ids'

function getSeenRequestsStorageKey() {
    return getScopedStorageKey(SEEN_REQUESTS_KEY_PREFIX, 'seen')
}

function getSeenRequestIds() {
    if (typeof localStorage === 'undefined') {
        return new Set()
    }

    const stored = safeReadJson(localStorage, getSeenRequestsStorageKey(), [])
    return Array.isArray(stored) ? new Set(stored) : new Set()
}

function markRequestsAsSeen(requestIds) {
    if (typeof localStorage === 'undefined') {
        return
    }

    const seen = getSeenRequestIds()
    requestIds.forEach((id) => seen.add(id))
    safeWriteJson(localStorage, getSeenRequestsStorageKey(), [...seen])
}

function cleanupSeenIds(allPendingIds) {
    if (typeof localStorage === 'undefined') {
        return
    }

    const seen = getSeenRequestIds()
    const stillRelevant = [...seen].filter((id) => allPendingIds.has(id))
    safeWriteJson(localStorage, getSeenRequestsStorageKey(), stillRelevant)
}

export default function MarketplaceNotificationBell() {
    const { t } = useTranslation()
    const { dir, isRTL, locale } = useRTL()
    const navigate = useNavigate()
    const [unseenCount, setUnseenCount] = useState(0)
    const [showDropdown, setShowDropdown] = useState(false)
    const [unseenRequests, setUnseenRequests] = useState([])
    const dropdownRef = useRef(null)
    const bellRef = useRef(null)
    const allPendingIdsRef = useRef([])
    const isAuthenticated = useAuthStore((state) => state.isAuthenticated)
    const user = useAuthStore((state) => state.user)
    const isMarketplaceActor = isServiceMarketplaceWorkflowActor(user, isAuthenticated)
    const actorScope = `${user?.tenantId || ''}:${user?.id || user?.userId || ''}:${user?.role || ''}`

    const fetchIncomingRequests = useCallback(async () => {
        try {
            if (!isMarketplaceActor) {
                setUnseenCount(0)
                setUnseenRequests([])
                return
            }

            const response = await api.get('/marketplace/incoming-requests')
            const data = response.data || []
            const pending = data.filter((request) => request.status === 'Pending')
            const pendingIds = new Set(pending.map((request) => request.requestId))
            allPendingIdsRef.current = pending.map((request) => request.requestId)

            cleanupSeenIds(pendingIds)

            const seen = getSeenRequestIds()
            const unseen = pending.filter((request) => !seen.has(request.requestId))

            setUnseenCount(unseen.length)
            setUnseenRequests(unseen.slice(0, 5))
        } catch {
            setUnseenCount(0)
            setUnseenRequests([])
        }
    }, [actorScope, isMarketplaceActor])

    useEffect(() => {
        setUnseenCount(0)
        setUnseenRequests([])
        setShowDropdown(false)
        fetchIncomingRequests()
        const interval = setInterval(fetchIncomingRequests, 30000)
        return () => clearInterval(interval)
    }, [actorScope, fetchIncomingRequests])

    useEffect(() => {
        const handleClickOutside = (event) => {
            if (
                dropdownRef.current &&
                !dropdownRef.current.contains(event.target) &&
                bellRef.current &&
                !bellRef.current.contains(event.target)
            ) {
                setShowDropdown(false)
            }
        }

        document.addEventListener('mousedown', handleClickOutside)
        return () => document.removeEventListener('mousedown', handleClickOutside)
    }, [])

    const markAllAsSeen = useCallback(() => {
        if (allPendingIdsRef.current.length > 0) {
            markRequestsAsSeen(allPendingIdsRef.current)
        }
        setUnseenCount(0)
        setUnseenRequests([])
        setShowDropdown(false)
    }, [])

    const handleBellClick = () => {
        if (unseenCount > 0) {
            setShowDropdown((current) => !current)
            return
        }

        navigate('/marketplace?tab=incoming')
    }

    const handleViewAll = () => {
        markAllAsSeen()
        navigate('/marketplace?tab=incoming')
    }

    const handleRequestClick = () => {
        markAllAsSeen()
        navigate('/marketplace?tab=incoming')
    }

    const getTimeAgo = useCallback((dateStr) => {
        const date = new Date(dateStr)
        if (Number.isNaN(date.getTime())) {
            return ''
        }

        const relativeFormatter = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' })
        const diffMs = date.getTime() - Date.now()
        const absDiffMs = Math.abs(diffMs)

        if (absDiffMs < 60000) {
            return relativeFormatter.format(0, 'second')
        }

        if (absDiffMs < 3600000) {
            return relativeFormatter.format(Math.round(diffMs / 60000), 'minute')
        }

        if (absDiffMs < 86400000) {
            return relativeFormatter.format(Math.round(diffMs / 3600000), 'hour')
        }

        return relativeFormatter.format(Math.round(diffMs / 86400000), 'day')
    }, [locale])

    if (!isMarketplaceActor) {
        return null
    }

    return (
        <div className="relative" dir={dir}>
            <button
                ref={bellRef}
                onClick={handleBellClick}
                className="relative rounded-lg p-2 text-gray-400 transition-colors hover:bg-gray-100 hover:text-gray-600"
                title={unseenCount > 0
                    ? t('servicesMarketplace.notifications.newRequestsTitle', { count: unseenCount })
                    : t('servicesMarketplace.notifications.marketplaceRequests')}
                type="button"
            >
                <svg className="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={2}
                        d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9"
                    />
                </svg>

                {unseenCount > 0 ? (
                    <span className={`absolute -top-0.5 flex h-5 min-w-[20px] items-center justify-center rounded-full bg-red-500 px-1.5 text-xs font-bold text-white shadow-sm animate-pulse ${isRTL ? '-left-0.5' : '-right-0.5'}`}>
                        {unseenCount > 9 ? '9+' : unseenCount}
                    </span>
                ) : null}
            </button>

            {showDropdown && unseenCount > 0 ? (
                <div
                    ref={dropdownRef}
                    className={`absolute z-50 mt-2 w-80 overflow-hidden rounded-xl border border-gray-200 bg-white shadow-2xl ${isRTL ? 'left-0' : 'right-0'}`}
                    style={{ animation: 'fadeInDown 0.2s ease-out' }}
                >
                    <div className="bg-gradient-to-r from-indigo-600 to-purple-600 px-4 py-3 text-white">
                        <div className="flex items-center justify-between gap-3">
                            <span className="font-semibold text-sm">{t('servicesMarketplace.incoming.title')}</span>
                            <span className="rounded-full bg-white/20 px-2.5 py-1 text-xs font-bold text-white">
                                {t('servicesMarketplace.notifications.newCount', { count: unseenCount })}
                            </span>
                        </div>
                    </div>

                    <div className="max-h-72 divide-y divide-gray-100 overflow-y-auto">
                        {unseenRequests.map((request) => (
                            <button
                                key={request.requestId}
                                onClick={handleRequestClick}
                                className="group w-full px-4 py-3 text-start transition-colors hover:bg-indigo-50"
                                type="button"
                            >
                                <div className="flex items-start gap-3">
                                    <div className="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-amber-400 to-orange-500 text-sm font-semibold text-white shadow-sm">
                                        {request.serviceTitle?.trim()?.charAt(0)?.toUpperCase() || 'S'}
                                    </div>
                                    <div className="min-w-0 flex-1">
                                        <p className="truncate text-sm font-medium text-gray-900 transition-colors group-hover:text-indigo-700">
                                            {request.serviceTitle || t('servicesMarketplace.requestCard.serviceRequest')}
                                        </p>
                                        <div className="mt-0.5 flex items-center gap-2">
                                            <span className="inline-flex items-center rounded bg-amber-100 px-1.5 py-0.5 text-[10px] font-semibold text-amber-800">
                                                {t('servicesMarketplace.status.pending')}
                                            </span>
                                            <span className="text-xs text-gray-400">
                                                {getTimeAgo(request.createdAt)}
                                            </span>
                                        </div>
                                    </div>
                                    <svg className={`mt-1 h-4 w-4 flex-shrink-0 text-gray-300 transition-colors group-hover:text-indigo-500 ${isRTL ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                                    </svg>
                                </div>
                            </button>
                        ))}
                    </div>

                    <div className="border-t border-gray-100 bg-gray-50 px-4 py-2.5">
                        <button
                            onClick={handleViewAll}
                            className="w-full py-1 text-center text-sm font-semibold text-indigo-600 transition-colors hover:text-indigo-800"
                            type="button"
                        >
                            {t('servicesMarketplace.notifications.viewAll')}
                        </button>
                    </div>
                </div>
            ) : null}

            <style>{`
                @keyframes fadeInDown {
                    from {
                        opacity: 0;
                        transform: translateY(-8px);
                    }
                    to {
                        opacity: 1;
                        transform: translateY(0);
                    }
                }
            `}</style>
        </div>
    )
}

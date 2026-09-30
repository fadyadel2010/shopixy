import React, { useCallback, useDeferredValue, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import useFormatting from '../hooks/useFormatting'
import api, { isRequestCanceled } from '../services/api'

const SESSION_STATUSES = [
    { value: 'Abandoned', key: 'abandoned', color: 'bg-red-100 text-red-800' },
    { value: 'Active', key: 'active', color: 'bg-yellow-100 text-yellow-800' },
    { value: 'ConvertedToOrder', key: 'converted', color: 'bg-green-100 text-green-800' },
]

const DEVICE_KEYS = {
    Mobile: 'mobile',
    Desktop: 'desktop',
    Tablet: 'tablet',
}

function getStatusInfo(value) {
    return SESSION_STATUSES.find((status) => status.value === value) || {
        key: 'unknown',
        color: 'bg-gray-100 text-gray-800',
    }
}

function getErrorType(error) {
    if (error?.response?.status === 403) return 'permission'
    if (error?.response?.status === 401) return 'authentication'
    return 'load'
}

export default function AbandonedCheckouts() {
    const { t } = useTranslation()
    const { dir, locale, formatCurrency, formatDateTime, formatInteger, formatNumber } = useFormatting()
    const requestSequence = useRef(0)
    const detailRequestSequence = useRef(0)
    const hasLoadedList = useRef(false)

    const [checkouts, setCheckouts] = useState([])
    const [loading, setLoading] = useState(true)
    const [refreshing, setRefreshing] = useState(false)
    const [listError, setListError] = useState(null)
    const [pagination, setPagination] = useState({
        page: 1,
        pageSize: 20,
        totalCount: 0,
        totalPages: 0,
        hasMore: false,
    })
    const [statusFilter, setStatusFilter] = useState('Abandoned')
    const [search, setSearch] = useState('')
    const deferredSearch = useDeferredValue(search.trim())
    const [selectedSessionId, setSelectedSessionId] = useState(null)
    const [selectedCheckout, setSelectedCheckout] = useState(null)
    const [detailLoading, setDetailLoading] = useState(false)
    const [detailError, setDetailError] = useState(null)
    const [copyComplete, setCopyComplete] = useState(false)
    const [stats, setStats] = useState(null)
    const [statsLoading, setStatsLoading] = useState(true)
    const [statsError, setStatsError] = useState(null)

    const formatPercentage = (value) => `${formatNumber(value ?? 0, {
        minimumFractionDigits: 1,
        maximumFractionDigits: 1,
    })}%`

    const formatRelativeTime = (dateString) => {
        if (!dateString) return t('common.notAvailable')

        const date = new Date(dateString)
        if (Number.isNaN(date.getTime())) return t('common.notAvailable')

        const seconds = Math.round((date.getTime() - Date.now()) / 1000)
        const formatter = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' })
        if (Math.abs(seconds) < 60) return formatter.format(seconds, 'second')
        const minutes = Math.round(seconds / 60)
        if (Math.abs(minutes) < 60) return formatter.format(minutes, 'minute')
        const hours = Math.round(minutes / 60)
        if (Math.abs(hours) < 24) return formatter.format(hours, 'hour')
        const days = Math.round(hours / 24)
        if (Math.abs(days) < 7) return formatter.format(days, 'day')
        return formatDateTime(date)
    }

    const formatTimeSpent = (seconds) => {
        if (seconds == null) return t('common.notAvailable')

        const totalSeconds = Math.max(0, Math.round(seconds))
        if (totalSeconds < 60) return t('abandonedCheckouts.durationSeconds', { count: totalSeconds })
        const minutes = Math.floor(totalSeconds / 60)
        if (minutes < 60) {
            return t('abandonedCheckouts.durationMinutesSeconds', {
                minutes,
                seconds: totalSeconds % 60,
            })
        }
        return t('abandonedCheckouts.durationHoursMinutes', {
            hours: Math.floor(minutes / 60),
            minutes: minutes % 60,
        })
    }

    const getSourceLabel = (source) => {
        const key = String(source || 'unknown').toLowerCase().replace(/[^a-z]/g, '')
        const knownSources = ['direct', 'storefront', 'instagram', 'affiliate', 'googleads']
        return knownSources.includes(key) ? t(`abandonedCheckouts.sources.${key}`) : (source || t('abandonedCheckouts.sources.unknown'))
    }

    const fetchCheckouts = useCallback(async () => {
        const sequence = ++requestSequence.current
        if (hasLoadedList.current) setRefreshing(true)
        else setLoading(true)
        setListError(null)

        try {
            const params = new URLSearchParams({
                page: pagination.page.toString(),
                pageSize: pagination.pageSize.toString(),
            })
            if (statusFilter) params.append('status', statusFilter)
            if (deferredSearch) params.append('search', deferredSearch)

            const response = await api.get(`/admin/abandoned-checkouts?${params.toString()}`)
            if (sequence !== requestSequence.current) return

            const data = response.data
            setCheckouts(data.items || [])
            setPagination((previous) => ({
                ...previous,
                totalCount: data.totalCount ?? 0,
                totalPages: data.totalPages ?? 0,
                hasMore: Boolean(data.hasMore),
            }))
            hasLoadedList.current = true
        } catch (error) {
            if (sequence !== requestSequence.current || isRequestCanceled(error)) return
            setListError(getErrorType(error))
            if (!hasLoadedList.current) setCheckouts([])
        } finally {
            if (sequence === requestSequence.current) {
                setLoading(false)
                setRefreshing(false)
            }
        }
    }, [deferredSearch, pagination.page, pagination.pageSize, statusFilter])

    const fetchStats = useCallback(async () => {
        setStatsLoading(true)
        setStatsError(null)
        try {
            const response = await api.get('/admin/abandoned-checkouts/stats')
            setStats(response.data)
        } catch (error) {
            if (!isRequestCanceled(error)) setStatsError(getErrorType(error))
        } finally {
            setStatsLoading(false)
        }
    }, [])

    const fetchDetail = useCallback(async (sessionId) => {
        const sequence = ++detailRequestSequence.current
        setSelectedSessionId(sessionId)
        setSelectedCheckout(null)
        setDetailLoading(true)
        setDetailError(null)
        setCopyComplete(false)
        try {
            const response = await api.get(`/admin/abandoned-checkouts/${sessionId}`)
            if (sequence === detailRequestSequence.current) setSelectedCheckout(response.data)
        } catch (error) {
            if (sequence === detailRequestSequence.current && !isRequestCanceled(error)) {
                setDetailError(getErrorType(error))
            }
        } finally {
            if (sequence === detailRequestSequence.current) setDetailLoading(false)
        }
    }, [])

    useEffect(() => {
        void fetchCheckouts()
    }, [fetchCheckouts])

    useEffect(() => {
        void fetchStats()
    }, [fetchStats])

    const closeDetail = () => {
        detailRequestSequence.current += 1
        setSelectedSessionId(null)
        setSelectedCheckout(null)
        setDetailError(null)
    }

    const listErrorKey = listError === 'permission'
        ? 'abandonedCheckouts.errors.permission'
        : listError === 'authentication'
            ? 'abandonedCheckouts.errors.authentication'
            : checkouts.length > 0
                ? 'abandonedCheckouts.errors.refresh'
                : 'abandonedCheckouts.errors.list'
    const statsErrorKey = statsError === 'permission'
        ? 'abandonedCheckouts.errors.permission'
        : statsError === 'authentication'
            ? 'abandonedCheckouts.errors.authentication'
            : 'abandonedCheckouts.errors.stats'
    const hasConstraints = Boolean(statusFilter || deferredSearch)
    const detailItems = selectedCheckout?.items || []

    return (
        <div className="p-4 sm:p-6 space-y-6" dir={dir}>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('abandonedCheckouts.title')}</h1>
                    <p className="mt-1 text-sm text-gray-500">{t('abandonedCheckouts.subtitle')}</p>
                </div>
                <button
                    type="button"
                    onClick={() => { void fetchCheckouts(); void fetchStats() }}
                    disabled={refreshing || statsLoading}
                    className="inline-flex items-center justify-center rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 shadow-sm hover:bg-gray-50 disabled:cursor-wait disabled:opacity-60"
                >
                    {refreshing || statsLoading ? t('abandonedCheckouts.refreshing') : t('common.refresh')}
                </button>
            </div>

            {statsError ? (
                <div className="flex flex-col gap-3 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 sm:flex-row sm:items-center sm:justify-between" role="alert">
                    <span>{t(statsErrorKey)}</span>
                    <button type="button" onClick={() => void fetchStats()} className="font-semibold underline">{t('common.retry')}</button>
                </div>
            ) : (
                <div className="grid grid-cols-2 gap-4 md:grid-cols-4" aria-busy={statsLoading}>
                    {[
                        ['totalSessions', t('abandonedCheckouts.totalSessions'), stats ? formatInteger(stats.totalSessions ?? 0) : t('common.loading')],
                        ['abandoned', t('abandonedCheckouts.abandoned'), stats ? `${formatInteger(stats.abandonedSessions ?? 0)} (${formatPercentage(stats.abandonmentRate)})` : t('common.loading')],
                        ['converted', t('abandonedCheckouts.converted'), stats ? `${formatInteger(stats.convertedSessions ?? 0)} (${formatPercentage(stats.conversionRate)})` : t('common.loading')],
                        ['value', t('abandonedCheckouts.lostRevenue'), stats
                            ? stats.abandonedValueAvailable === false
                                ? t('common.notAvailable')
                                : formatCurrency(stats.totalAbandonedRevenue ?? 0, stats.abandonedValueCurrency || undefined)
                            : t('common.loading')],
                    ].map(([key, label, value]) => (
                        <div key={key} className="rounded-xl border border-gray-100 bg-white p-4 shadow-sm sm:p-5">
                            <p className="text-sm text-gray-500">{label}</p>
                            <p className="mt-1 text-xl font-bold text-gray-900 sm:text-2xl">{value}</p>
                            {key === 'value' && <p className="mt-1 text-xs text-gray-500">{t('abandonedCheckouts.estimatedValueHint')}</p>}
                        </div>
                    ))}
                </div>
            )}

            <div className="rounded-xl border border-gray-100 bg-white p-4 shadow-sm">
                <div className="flex flex-col gap-4 md:flex-row">
                    <div className="flex flex-wrap gap-2" role="group" aria-label={t('abandonedCheckouts.statusFilter')}>
                        <button
                            type="button"
                            aria-pressed={!statusFilter}
                            onClick={() => { setStatusFilter(''); setPagination((previous) => ({ ...previous, page: 1 })) }}
                            className={`rounded-lg px-4 py-2 text-sm font-medium transition-colors ${!statusFilter ? 'bg-gray-900 text-white' : 'bg-gray-100 text-gray-700 hover:bg-gray-200'}`}
                        >
                            {t('abandonedCheckouts.statuses.all')}
                        </button>
                        {SESSION_STATUSES.map((status) => (
                            <button
                                type="button"
                                key={status.value}
                                aria-pressed={statusFilter === status.value}
                                onClick={() => { setStatusFilter(status.value); setPagination((previous) => ({ ...previous, page: 1 })) }}
                                className={`rounded-lg px-4 py-2 text-sm font-medium transition-colors ${statusFilter === status.value ? 'bg-gray-900 text-white' : 'bg-gray-100 text-gray-700 hover:bg-gray-200'}`}
                            >
                                {t(`abandonedCheckouts.statuses.${status.key}`)}
                            </button>
                        ))}
                    </div>
                    <div className="flex-1">
                        <label htmlFor="abandoned-checkout-search" className="sr-only">{t('abandonedCheckouts.searchLabel')}</label>
                        <input
                            id="abandoned-checkout-search"
                            type="search"
                            placeholder={t('abandonedCheckouts.searchPlaceholder')}
                            value={search}
                            onChange={(event) => { setSearch(event.target.value); setPagination((previous) => ({ ...previous, page: 1 })) }}
                            className="w-full rounded-lg border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                        />
                    </div>
                </div>
            </div>

            {listError && (
                <div className="flex flex-col gap-3 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 sm:flex-row sm:items-center sm:justify-between" role="alert">
                    <span>{t(listErrorKey)}</span>
                    <button type="button" onClick={() => void fetchCheckouts()} className="font-semibold underline">{t('common.retry')}</button>
                </div>
            )}

            <div className="overflow-hidden rounded-xl border border-gray-100 bg-white shadow-sm" aria-busy={loading || refreshing}>
                {refreshing && <div className="border-b border-indigo-100 bg-indigo-50 px-4 py-2 text-sm text-indigo-700" role="status">{t('abandonedCheckouts.refreshing')}</div>}
                {loading ? (
                    <div className="flex items-center justify-center gap-3 py-20" role="status">
                        <div className="h-10 w-10 animate-spin rounded-full border-b-2 border-indigo-500" />
                        <span className="sr-only">{t('abandonedCheckouts.loading')}</span>
                    </div>
                ) : !listError && checkouts.length === 0 ? (
                    <div className="px-4 py-20 text-center text-gray-500">
                        <p className="text-lg font-medium">{t(hasConstraints ? 'abandonedCheckouts.noMatches' : 'abandonedCheckouts.empty')}</p>
                        <p className="mt-1 text-sm">{t(hasConstraints ? 'abandonedCheckouts.noMatchesHint' : 'abandonedCheckouts.emptyHint')}</p>
                    </div>
                ) : checkouts.length > 0 ? (
                    <div className="max-w-full overflow-x-auto">
                        <table className="min-w-[880px] w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                                <tr>
                                    {[
                                        t('common.customer'), t('common.status'), t('abandonedCheckouts.cartTotal'),
                                        t('abandonedCheckouts.device'), t('abandonedCheckouts.source'),
                                        t('abandonedCheckouts.timeSpent'), t('abandonedCheckouts.lastActivity'), t('common.actions'),
                                    ].map((heading) => <th key={heading} className="px-4 py-3 text-start text-xs font-medium uppercase tracking-wider text-gray-500">{heading}</th>)}
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-200 bg-white">
                                {checkouts.map((checkout) => {
                                    const statusInfo = getStatusInfo(checkout.status)
                                    return (
                                        <tr key={checkout.checkoutSessionId} className="cursor-pointer transition-colors hover:bg-gray-50" onClick={() => void fetchDetail(checkout.checkoutSessionId)}>
                                            <td className="px-4 py-3">
                                                <div className="flex flex-col">
                                                    <span className="text-sm font-medium text-gray-900">{checkout.customerName || t('abandonedCheckouts.unknownCustomer')}</span>
                                                    <span className="text-xs text-gray-500" dir="ltr">{checkout.phone || checkout.email || t('common.notAvailable')}</span>
                                                </div>
                                            </td>
                                            <td className="px-4 py-3"><span className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-medium ${statusInfo.color}`}>{t(`abandonedCheckouts.statuses.${statusInfo.key}`)}</span></td>
                                            <td className="px-4 py-3 text-sm font-medium text-gray-900">{checkout.cartTotal == null ? t('common.notAvailable') : formatCurrency(checkout.cartTotal, checkout.currency || undefined)}</td>
                                            <td className="px-4 py-3 text-sm text-gray-600">{checkout.deviceType ? t(`abandonedCheckouts.devices.${DEVICE_KEYS[checkout.deviceType] || 'unknown'}`) : t('common.notAvailable')}</td>
                                            <td className="px-4 py-3 text-sm text-gray-600">{getSourceLabel(checkout.source)}</td>
                                            <td className="px-4 py-3 text-sm text-gray-600">{formatTimeSpent(checkout.timeSpentSeconds)}</td>
                                            <td className="px-4 py-3 text-sm text-gray-500">{formatRelativeTime(checkout.lastActivityAtUtc)}</td>
                                            <td className="px-4 py-3"><button type="button" onClick={(event) => { event.stopPropagation(); void fetchDetail(checkout.checkoutSessionId) }} className="text-sm font-medium text-indigo-600 hover:text-indigo-800">{t('common.view')}</button></td>
                                        </tr>
                                    )
                                })}
                            </tbody>
                        </table>
                    </div>
                ) : null}

                {pagination.totalPages > 1 && !listError && (
                    <div className="flex flex-col gap-3 border-t border-gray-200 bg-gray-50 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                        <p className="text-sm text-gray-700">{t('abandonedCheckouts.pagination', {
                            from: formatInteger(((pagination.page - 1) * pagination.pageSize) + 1),
                            to: formatInteger(Math.min(pagination.page * pagination.pageSize, pagination.totalCount)),
                            total: formatInteger(pagination.totalCount),
                        })}</p>
                        <div className="flex gap-2">
                            <button type="button" disabled={pagination.page <= 1} onClick={() => setPagination((previous) => ({ ...previous, page: previous.page - 1 }))} className="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50">{t('common.previous')}</button>
                            <button type="button" disabled={pagination.page >= pagination.totalPages || !pagination.hasMore} onClick={() => setPagination((previous) => ({ ...previous, page: previous.page + 1 }))} className="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50">{t('common.next')}</button>
                        </div>
                    </div>
                )}
            </div>

            {selectedSessionId && (
                <div className="fixed inset-0 z-50 flex justify-end" role="dialog" aria-modal="true" aria-labelledby="checkout-detail-title">
                    <button type="button" className="absolute inset-0 bg-black/30" onClick={closeDetail} aria-label={t('common.close')} />
                    <div className="relative h-full w-full max-w-lg overflow-y-auto bg-white shadow-xl">
                        <div className="p-5 sm:p-6">
                            <div className="flex items-center justify-between gap-4">
                                <h2 id="checkout-detail-title" className="text-lg font-bold text-gray-900">{t('abandonedCheckouts.details')}</h2>
                                <button type="button" onClick={closeDetail} className="rounded-md p-2 hover:bg-gray-100" aria-label={t('common.close')}>{t('common.close')}</button>
                            </div>

                            {detailLoading && <div className="flex items-center justify-center gap-3 py-20" role="status"><div className="h-10 w-10 animate-spin rounded-full border-b-2 border-indigo-500" /><span className="sr-only">{t('abandonedCheckouts.loadingDetails')}</span></div>}
                            {detailError && !detailLoading && (
                                <div className="mt-6 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700" role="alert">
                                    <p>{t(detailError === 'permission' ? 'abandonedCheckouts.errors.permission' : detailError === 'authentication' ? 'abandonedCheckouts.errors.authentication' : 'abandonedCheckouts.errors.detail')}</p>
                                    <button type="button" onClick={() => void fetchDetail(selectedSessionId)} className="mt-2 font-semibold underline">{t('common.retry')}</button>
                                </div>
                            )}
                            {selectedCheckout && !detailLoading && (
                                <div className="mt-6 space-y-6">
                                    <span className={`inline-flex rounded-full px-3 py-1.5 text-sm font-medium ${getStatusInfo(selectedCheckout.status).color}`}>{t(`abandonedCheckouts.statuses.${getStatusInfo(selectedCheckout.status).key}`)}</span>

                                    {selectedCheckout.status === 'ConvertedToOrder' && selectedCheckout.convertedOrderNumber && <div className="rounded-lg border border-green-200 bg-green-50 p-4 text-sm text-green-800">{t('abandonedCheckouts.convertedOrder', { orderNumber: selectedCheckout.convertedOrderNumber })}</div>}

                                    <section className="space-y-3 rounded-lg bg-gray-50 p-4">
                                        <h3 className="text-sm font-semibold uppercase tracking-wider text-gray-700">{t('common.customer')}</h3>
                                        <div className="grid grid-cols-1 gap-3 text-sm sm:grid-cols-2">
                                            {[
                                                [t('common.name'), selectedCheckout.customerName],
                                                [t('common.phone'), selectedCheckout.phone, 'ltr'],
                                                [t('common.email'), selectedCheckout.email, 'ltr'],
                                                [t('common.city'), selectedCheckout.city],
                                            ].map(([label, value, valueDir]) => <div key={label}><span className="text-gray-500">{label}</span><p className="font-medium text-gray-900" dir={valueDir}>{value || t('common.notAvailable')}</p></div>)}
                                        </div>
                                        {selectedCheckout.address && <div className="text-sm"><span className="text-gray-500">{t('common.address')}</span><p className="font-medium text-gray-900">{selectedCheckout.address}</p></div>}
                                        {selectedCheckout.notes && <div className="text-sm"><span className="text-gray-500">{t('common.notes')}</span><p className="text-gray-700">{selectedCheckout.notes}</p></div>}
                                    </section>

                                    <section className="space-y-3 rounded-lg bg-gray-50 p-4">
                                        <h3 className="text-sm font-semibold uppercase tracking-wider text-gray-700">{t('abandonedCheckouts.checkoutInfo')}</h3>
                                        <div className="grid grid-cols-1 gap-3 text-sm sm:grid-cols-2">
                                            <div><span className="text-gray-500">{t('abandonedCheckouts.cartTotal')}</span><p className="text-lg font-bold text-gray-900">{selectedCheckout.cartTotal == null ? t('common.notAvailable') : formatCurrency(selectedCheckout.cartTotal, selectedCheckout.currency || undefined)}</p></div>
                                            <div><span className="text-gray-500">{t('abandonedCheckouts.paymentMethod')}</span><p className="font-medium text-gray-900" dir="ltr">{selectedCheckout.paymentMethodCode || t('common.notAvailable')}</p></div>
                                            <div><span className="text-gray-500">{t('abandonedCheckouts.device')}</span><p className="font-medium text-gray-900">{selectedCheckout.deviceType ? t(`abandonedCheckouts.devices.${DEVICE_KEYS[selectedCheckout.deviceType] || 'unknown'}`) : t('common.notAvailable')}</p></div>
                                            <div><span className="text-gray-500">{t('abandonedCheckouts.source')}</span><p className="font-medium text-gray-900">{getSourceLabel(selectedCheckout.source)}</p></div>
                                        </div>
                                    </section>

                                    {selectedCheckout.recoveryUrl && selectedCheckout.status === 'Abandoned' && (
                                        <section className="rounded-lg border border-indigo-200 bg-indigo-50 p-4">
                                            <h3 className="text-sm font-semibold text-indigo-800">{t('abandonedCheckouts.recoveryLink')}</h3>
                                            <p className="mt-1 text-xs text-indigo-700">{t('abandonedCheckouts.recoveryLinkHint')}</p>
                                            <div className="mt-3 flex flex-col gap-2 sm:flex-row">
                                                <input type="text" readOnly value={selectedCheckout.recoveryUrl} dir="ltr" aria-label={t('abandonedCheckouts.recoveryLink')} className="min-w-0 flex-1 rounded border border-indigo-200 bg-white px-3 py-2 font-mono text-xs text-gray-700" />
                                                <button type="button" onClick={async () => { await navigator.clipboard.writeText(selectedCheckout.recoveryUrl); setCopyComplete(true) }} className="rounded bg-indigo-600 px-3 py-2 text-xs font-medium text-white hover:bg-indigo-700">{copyComplete ? t('abandonedCheckouts.copied') : t('common.copy')}</button>
                                            </div>
                                        </section>
                                    )}

                                    <section className="space-y-3 rounded-lg bg-gray-50 p-4">
                                        <h3 className="text-sm font-semibold uppercase tracking-wider text-gray-700">{t('common.timeline')}</h3>
                                        <dl className="space-y-2 text-sm">
                                            <div className="flex justify-between gap-4"><dt className="text-gray-500">{t('abandonedCheckouts.started')}</dt><dd className="text-end text-gray-900">{selectedCheckout.createdAtUtc ? formatDateTime(selectedCheckout.createdAtUtc) : t('common.notAvailable')}</dd></div>
                                            <div className="flex justify-between gap-4"><dt className="text-gray-500">{t('abandonedCheckouts.lastActivity')}</dt><dd className="text-end text-gray-900">{selectedCheckout.lastActivityAtUtc ? formatDateTime(selectedCheckout.lastActivityAtUtc) : t('common.notAvailable')}</dd></div>
                                            <div className="flex justify-between gap-4"><dt className="text-gray-500">{t('abandonedCheckouts.duration')}</dt><dd className="font-medium text-indigo-600">{formatTimeSpent(selectedCheckout.timeSpentSeconds)}</dd></div>
                                        </dl>
                                    </section>

                                    <section className="space-y-3 rounded-lg bg-gray-50 p-4">
                                        <h3 className="text-sm font-semibold uppercase tracking-wider text-gray-700">{t('abandonedCheckouts.cartItems', { count: detailItems.length })}</h3>
                                        {detailItems.length === 0 ? <p className="text-sm text-gray-500">{t('abandonedCheckouts.itemsUnavailable')}</p> : <div className="space-y-2">{detailItems.map((item, index) => (
                                            <div key={`${item.variantId || item.productName}-${index}`} className="flex items-center gap-3 rounded-lg border border-gray-100 bg-white p-3">
                                                {item.imageUrl ? <img src={item.imageUrl} alt="" className="h-12 w-12 rounded-md bg-gray-100 object-cover" /> : <div className="flex h-12 w-12 items-center justify-center rounded-md bg-gray-100 text-xs text-gray-400">{t('abandonedCheckouts.item')}</div>}
                                                <div className="min-w-0 flex-1"><p className="truncate text-sm font-medium text-gray-900">{item.productName || t('abandonedCheckouts.unknownItem')}</p>{item.variantName && <p className="text-xs text-gray-500">{item.variantName}</p>}</div>
                                                <div className="flex-shrink-0 text-end"><p className="text-sm font-medium text-gray-900">{formatCurrency((item.price ?? 0) * (item.quantity ?? 0), selectedCheckout.currency || undefined)}</p><p className="text-xs text-gray-500">{t('abandonedCheckouts.itemQuantity', { count: item.quantity ?? 0 })}</p></div>
                                            </div>
                                        ))}</div>}
                                    </section>
                                </div>
                            )}
                        </div>
                    </div>
                </div>
            )}
        </div>
    )
}

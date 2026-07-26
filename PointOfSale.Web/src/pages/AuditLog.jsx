import { Fragment, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { format } from 'date-fns'
import api from '../services/api'
import { useAuthStore } from '../store/authStore'
import { useDebounce } from '../hooks/useDebounce'
import { useRTL } from '../hooks/useRTL'
import { formatDateTime } from '../utils/dateFormat'

const TIMESTAMP_OPTIONS = {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
}

export default function AuditLog() {
    const { t } = useTranslation()
    const { dir, isRTL, locale } = useRTL()
    const { user } = useAuthStore()
    const [logs, setLogs] = useState([])
    const [loading, setLoading] = useState(true)
    const [expandedRow, setExpandedRow] = useState(null)
    const [filter, setFilter] = useState({
        page: 1,
        pageSize: 20,
        search: '',
        fromDate: '',
        toDate: '',
        userId: '',
        actionType: '',
        entityType: '',
        success: '',
    })
    const [totalCount, setTotalCount] = useState(0)
    const [totalPages, setTotalPages] = useState(0)

    const debouncedSearch = useDebounce(filter.search, 500)
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const actionAlignClass = isRTL ? 'text-left' : 'text-right'

    useEffect(() => {
        fetchLogs()
    }, [filter.page, filter.pageSize, debouncedSearch, filter.fromDate, filter.toDate, filter.actionType, filter.entityType, filter.success, filter.userId])

    const fetchLogs = async () => {
        setLoading(true)
        try {
            const params = {
                page: filter.page,
                pageSize: filter.pageSize,
                ...(debouncedSearch && { search: debouncedSearch }),
                ...(filter.fromDate && { fromDate: filter.fromDate }),
                ...(filter.toDate && { toDate: filter.toDate }),
                ...(filter.actionType && { actionType: filter.actionType }),
                ...(filter.entityType && { entityType: filter.entityType }),
                ...(filter.success !== '' && { success: filter.success === 'true' }),
                ...(filter.userId && { userId: filter.userId }),
            }

            const response = await api.get('/audit-logs', {
                params,
                headers: {
                    'Cache-Control': 'no-store',
                    Pragma: 'no-cache',
                },
            })
            setLogs(response.data.items)
            setTotalCount(response.data.totalCount)
            setTotalPages(response.data.totalPages)
        } catch (error) {
            console.error('Failed to fetch audit logs', error)
        } finally {
            setLoading(false)
        }
    }

    const handleExport = async () => {
        try {
            const params = {
                ...(filter.fromDate && { fromDate: filter.fromDate }),
                ...(filter.toDate && { toDate: filter.toDate }),
                ...(filter.actionType && { actionType: filter.actionType }),
                ...(filter.entityType && { entityType: filter.entityType }),
            }
            const response = await api.get('/audit-logs/export', {
                params,
                headers: {
                    'Cache-Control': 'no-store',
                    Pragma: 'no-cache',
                },
                responseType: 'blob',
            })

            const url = window.URL.createObjectURL(new Blob([response.data]))
            const link = document.createElement('a')
            link.href = url
            link.setAttribute('download', `audit_logs_${format(new Date(), 'yyyyMMdd')}.csv`)
            document.body.appendChild(link)
            link.click()
            link.remove()
        } catch (error) {
            console.error('Failed to export logs', error)
        }
    }

    const getActionColor = (action) => {
        switch (action?.toLowerCase()) {
            case 'create':
                return 'bg-green-100 text-green-800'
            case 'update':
                return 'bg-blue-100 text-blue-800'
            case 'delete':
                return 'bg-red-100 text-red-800'
            case 'inactivate':
                return 'bg-yellow-100 text-yellow-800'
            case 'post':
                return 'bg-purple-100 text-purple-800'
            case 'unpost':
                return 'bg-pink-100 text-pink-800'
            case 'login':
                return 'bg-indigo-100 text-indigo-800'
            case 'error':
                return 'bg-red-200 text-red-900'
            default:
                return 'bg-gray-100 text-gray-800'
        }
    }

    const getActionLabel = (action) => {
        if (!action) {
            return t('common.notApplicable')
        }

        const actionKey = action.toLowerCase()
        const translatedAction = t(`auditLog.actions.${actionKey}`)
        return translatedAction !== `auditLog.actions.${actionKey}` ? translatedAction : action
    }

    const toggleRow = (id) => {
        setExpandedRow((current) => (current === id ? null : id))
    }

    const formatAuditFieldLabel = (key) => {
        const translatedKey = t(`auditLog.fields.${key}`)
        if (translatedKey !== `auditLog.fields.${key}`) {
            return translatedKey
        }

        return key
            .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
            .replace(/_/g, ' ')
    }

    const formatJson = (jsonString) => {
        if (!jsonString) return null

        try {
            const parsed = JSON.parse(jsonString)

            if (typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)) {
                return (
                    <div className="overflow-hidden rounded border border-gray-200 bg-gray-50 text-xs">
                        <table className="min-w-full divide-y divide-gray-200">
                            <tbody className="divide-y divide-gray-200 bg-white">
                                {Object.entries(parsed).map(([key, value]) => (
                                    <tr key={key}>
                                        <td className={`w-1/3 whitespace-nowrap bg-gray-50 px-3 py-2 font-medium text-gray-500 ${tableTextAlignClass}`}>
                                            {formatAuditFieldLabel(key)}
                                        </td>
                                        <td className={`break-all whitespace-pre-wrap px-3 py-2 text-gray-800 ${tableTextAlignClass}`}>
                                            {typeof value === 'object' ? (
                                                <pre className="whitespace-pre-wrap">{JSON.stringify(value, null, 2)}</pre>
                                            ) : (
                                                String(value)
                                            )}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )
            }

            return (
                <pre className={`overflow-x-auto rounded border border-gray-200 bg-gray-50 p-2 text-xs ${tableTextAlignClass}`}>
                    {JSON.stringify(parsed, null, 2)}
                </pre>
            )
        } catch {
            return <span className="text-red-500">{t('auditLog.invalidJson')}</span>
        }
    }

    if (!user || (user.role !== 'Admin' && user.role !== 'SuperAdmin')) {
        return (
            <div dir={dir} className="flex h-full items-center justify-center">
                <div className="rounded-xl bg-red-50 p-8 text-center">
                    <h2 className="mb-2 text-2xl font-bold text-red-600">{t('auditLog.accessDenied')}</h2>
                    <p className="text-gray-600">{t('auditLog.accessDeniedMessage')}</p>
                </div>
            </div>
        )
    }

    return (
        <div dir={dir} className="space-y-6 p-6">
            <div className="flex flex-col items-start justify-between gap-4 md:flex-row md:items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800">{t('auditLog.title')}</h1>
                    <p className="text-gray-500">{t('auditLog.subtitle')}</p>
                </div>
                <button
                    onClick={handleExport}
                    className={`flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-white transition-colors hover:bg-indigo-700 ${isRTL ? 'flex-row-reverse' : ''}`}
                >
                    <span>{t('auditLog.export')}</span>
                    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                    </svg>
                </button>
            </div>

            <div className="grid grid-cols-1 gap-4 rounded-xl border border-gray-200 bg-white p-4 shadow-sm md:grid-cols-4 lg:grid-cols-6">
                <div className="col-span-2">
                    <label className="mb-1 block text-sm font-medium text-gray-700">{t('auditLog.search')}</label>
                    <input
                        type="text"
                        value={filter.search}
                        onChange={(e) => setFilter({ ...filter, search: e.target.value, page: 1 })}
                        placeholder={t('auditLog.searchPlaceholder')}
                        className="w-full rounded-lg border px-4 py-2 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
                <div>
                    <label className="mb-1 block text-sm font-medium text-gray-700">{t('auditLog.action')}</label>
                    <select
                        value={filter.actionType}
                        onChange={(e) => setFilter({ ...filter, actionType: e.target.value, page: 1 })}
                        className="w-full rounded-lg border px-4 py-2 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        <option value="Create">{t('auditLog.actions.create')}</option>
                        <option value="Update">{t('auditLog.actions.update')}</option>
                        <option value="Delete">{t('auditLog.actions.delete')}</option>
                        <option value="Login">{t('auditLog.actions.login')}</option>
                        <option value="Post">{t('auditLog.actions.post')}</option>
                        <option value="Inactivate">{t('auditLog.actions.inactivate')}</option>
                    </select>
                </div>
                <div>
                    <label className="mb-1 block text-sm font-medium text-gray-700">{t('auditLog.entity')}</label>
                    <input
                        type="text"
                        value={filter.entityType}
                        onChange={(e) => setFilter({ ...filter, entityType: e.target.value, page: 1 })}
                        placeholder={t('auditLog.entity')}
                        className="w-full rounded-lg border px-4 py-2 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
                <div>
                    <label className="mb-1 block text-sm font-medium text-gray-700">{t('auditLog.fromDate')}</label>
                    <input
                        type="date"
                        value={filter.fromDate}
                        onChange={(e) => setFilter({ ...filter, fromDate: e.target.value, page: 1 })}
                        className="w-full rounded-lg border px-4 py-2 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
                <div>
                    <label className="mb-1 block text-sm font-medium text-gray-700">{t('auditLog.toDate')}</label>
                    <input
                        type="date"
                        value={filter.toDate}
                        onChange={(e) => setFilter({ ...filter, toDate: e.target.value, page: 1 })}
                        className="w-full rounded-lg border px-4 py-2 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
            </div>

            <div className="overflow-hidden rounded-xl border border-gray-200 bg-white shadow-sm">
                <div className="overflow-x-auto">
                    <table className="w-full">
                        <thead className="border-b border-gray-200 bg-gray-50">
                            <tr>
                                <th className={`px-6 py-3 text-xs font-semibold uppercase tracking-wider text-gray-500 ${tableTextAlignClass}`}>{t('auditLog.timestamp')}</th>
                                <th className={`px-6 py-3 text-xs font-semibold uppercase tracking-wider text-gray-500 ${tableTextAlignClass}`}>{t('auditLog.user')}</th>
                                <th className={`px-6 py-3 text-xs font-semibold uppercase tracking-wider text-gray-500 ${tableTextAlignClass}`}>{t('auditLog.action')}</th>
                                <th className={`px-6 py-3 text-xs font-semibold uppercase tracking-wider text-gray-500 ${tableTextAlignClass}`}>{t('auditLog.entity')}</th>
                                <th className={`px-6 py-3 text-xs font-semibold uppercase tracking-wider text-gray-500 ${tableTextAlignClass}`}>{t('auditLog.description')}</th>
                                <th className={`px-6 py-3 text-xs font-semibold uppercase tracking-wider text-gray-500 ${tableTextAlignClass}`}>{t('auditLog.reference')}</th>
                                <th className={`px-6 py-3 text-xs font-semibold uppercase tracking-wider text-gray-500 ${actionAlignClass}`}></th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {loading ? (
                                <tr>
                                    <td colSpan="7" className="px-6 py-12 text-center text-gray-500">
                                        <div className="flex items-center justify-center">
                                            <div className="h-8 w-8 animate-spin rounded-full border-b-2 border-indigo-600"></div>
                                        </div>
                                    </td>
                                </tr>
                            ) : logs.length === 0 ? (
                                <tr>
                                    <td colSpan="7" className="px-6 py-12 text-center text-gray-500">
                                        {t('auditLog.noLogs')}
                                    </td>
                                </tr>
                            ) : (
                                logs.map((log) => {
                                    const rowId = log.logId || log.timestampUtc
                                    const isExpanded = expandedRow === rowId

                                    return (
                                        <Fragment key={rowId}>
                                            <tr
                                                className={`cursor-pointer transition-colors hover:bg-gray-50 ${isExpanded ? 'bg-indigo-50' : ''}`}
                                                onClick={() => toggleRow(rowId)}
                                            >
                                                <td className={`px-6 py-4 text-sm text-gray-600 ${tableTextAlignClass}`}>
                                                    {formatDateTime(log.timestampUtc, locale, TIMESTAMP_OPTIONS)}
                                                </td>
                                                <td className={`px-6 py-4 text-sm font-medium text-gray-900 ${tableTextAlignClass}`}>
                                                    {log.userNameSnapshot || t('auditLog.system')}
                                                </td>
                                                <td className={`px-6 py-4 ${tableTextAlignClass}`}>
                                                    <span className={`rounded-full px-2 py-1 text-xs font-semibold ${getActionColor(log.actionType)}`}>
                                                        {getActionLabel(log.actionType)}
                                                    </span>
                                                </td>
                                                <td className={`px-6 py-4 text-sm text-gray-600 ${tableTextAlignClass}`}>
                                                    {log.entityType} <span className="text-xs text-gray-400">#{log.entityId}</span>
                                                </td>
                                                <td className={`max-w-xs truncate px-6 py-4 text-sm text-gray-600 ${tableTextAlignClass}`}>
                                                    {log.description}
                                                </td>
                                                <td className={`px-6 py-4 text-sm text-gray-600 ${tableTextAlignClass}`}>
                                                    {log.referenceNumber || t('common.notApplicable')}
                                                </td>
                                                <td className={`px-6 py-4 text-sm text-gray-500 ${actionAlignClass}`}>
                                                    <button className="font-medium text-indigo-600 hover:text-indigo-900">
                                                        {t('auditLog.viewDetails')} {isExpanded ? '^' : 'v'}
                                                    </button>
                                                </td>
                                            </tr>
                                            {isExpanded && (
                                                <tr className="bg-gray-50 shadow-inner">
                                                    <td colSpan="7" className="px-6 py-4">
                                                        <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                                                            {log.beforeDataJson && (
                                                                <div>
                                                                    <h4 className={`mb-2 text-xs font-semibold uppercase text-gray-500 ${tableTextAlignClass}`}>
                                                                        {t('auditLog.before')}
                                                                    </h4>
                                                                    {formatJson(log.beforeDataJson)}
                                                                </div>
                                                            )}
                                                            {log.afterDataJson && (
                                                                <div>
                                                                    <h4 className={`mb-2 text-xs font-semibold uppercase text-gray-500 ${tableTextAlignClass}`}>
                                                                        {t('auditLog.after')}
                                                                    </h4>
                                                                    {formatJson(log.afterDataJson)}
                                                                </div>
                                                            )}
                                                            <div className={`col-span-2 mt-2 grid grid-cols-1 gap-4 border-t pt-2 text-xs text-gray-500 md:grid-cols-3 ${tableTextAlignClass}`}>
                                                                <div>{t('auditLog.ipAddress')}: {log.ipAddress || t('common.notApplicable')}</div>
                                                                <div>{t('auditLog.source')}: {log.source || t('auditLog.web')}</div>
                                                                <div>{t('auditLog.userAgent')}: {log.userAgent?.substring(0, 50) || t('common.notApplicable')}</div>
                                                            </div>
                                                        </div>
                                                    </td>
                                                </tr>
                                            )}
                                        </Fragment>
                                    )
                                })
                            )}
                        </tbody>
                    </table>
                </div>
            </div>

            <div className="flex flex-col items-center justify-between gap-4 border-t border-gray-200 bg-gray-50 px-6 py-4 md:flex-row">
                <span className="text-sm text-gray-600">
                    {t('auditLog.showingPage', { page: filter.page, total: totalPages, count: totalCount })}
                </span>
                <div className="flex gap-2">
                    <button
                        onClick={() => setFilter({ ...filter, page: filter.page - 1 })}
                        disabled={filter.page === 1}
                        className="rounded-lg border bg-white px-4 py-2 text-sm hover:bg-gray-50 disabled:opacity-50"
                    >
                        {t('sales.previous')}
                    </button>
                    <button
                        onClick={() => setFilter({ ...filter, page: filter.page + 1 })}
                        disabled={filter.page === totalPages}
                        className="rounded-lg border bg-white px-4 py-2 text-sm hover:bg-gray-50 disabled:opacity-50"
                    >
                        {t('sales.next')}
                    </button>
                </div>
            </div>
        </div>
    )
}

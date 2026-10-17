/**
 * D08 — History
 *
 * Constitutional source: DOMAIN-3.1 Canonical Blueprint §2.3 (C08), §2.4 (route: /storefront/domains/:id/history)
 *
 * Append-only audit log of all domain state transitions.
 *
 * DOMAIN-3.1-B: Backed by real READ backend data via useDomainHistory hook.
 */

import { useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { History, Clock } from 'lucide-react'

import {
    AuthoringButton,
    AuthoringEmptyState,
    AuthoringFeedback,
    AuthoringLoadingState,
    useAuthoringGuard,
} from '../../authoringPlatform'

import { useDomainHistory } from '../hooks'

const COLUMNS = [
    { key: 'timestampUtc', labelKey: 'storefront.domains.timestamp', fallbackLabel: 'Timestamp' },
    { key: 'actorType', labelKey: 'storefront.domains.actor', fallbackLabel: 'Actor' },
    { key: 'action', labelKey: 'storefront.domains.action', fallbackLabel: 'Action' },
    { key: 'afterSnapshotJson', labelKey: 'storefront.domains.details', fallbackLabel: 'Details' },
    { key: 'traceId', labelKey: 'storefront.domains.traceId', fallbackLabel: 'Trace ID' },
]

export function DomainHistoryPage() {
    const { t } = useTranslation()
    const { id } = useParams()

    const { data, error, isLoading, isError, isUnauthorized, isNotFound, isEmpty, isSuccess, goToPage, refetch } =
        useDomainHistory(id)

    const { requestNavigation } = useAuthoringGuard({ isDirty: false, isBusy: isLoading })

    const handleBack = () => {
        requestNavigation(`/storefront/domains/${id}`)
    }

    const entries = data?.items || []

    if (isNotFound) {
        return <div className="space-y-4">
            <AuthoringButton action={{ label: t('common.back', 'Back'), onClick: () => requestNavigation('/storefront/domains') }} />
            <AuthoringEmptyState title={t('storefront.domains.domainNotFound', 'Domain not found')} description={t('storefront.domains.domainNotFoundDesc', 'The requested domain could not be found.')} />
        </div>
    }

    return (
        <div className="space-y-4">
            <AuthoringButton
                action={{
                    label: t('common.back', 'Back'),
                    onClick: handleBack,
                }}
                defaultLabel={t('common.back', 'Back')}
            />

            <div className="flex items-center justify-between rounded-xl border border-gray-200 bg-white px-4 py-3 dark:border-gray-700 dark:bg-gray-800">
                <div>
                    <h1 className="text-xl font-semibold text-gray-900 dark:text-gray-100">
                        {t('storefront.domains.history', 'History')}
                    </h1>
                    <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
                        {t('storefront.domains.domainId', 'Domain ID')}:{' '}
                        <span className="font-mono text-gray-900 dark:text-gray-100 break-all">
                            {id || '—'}
                        </span>
                    </p>
                </div>
                <button
                    type="button"
                    disabled
                    className="flex items-center gap-2 rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-500 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-600 dark:text-gray-400"
                    aria-label={t('storefront.domains.dateRange', 'Date Range')}
                >
                    <Clock size={16} />
                    {t('storefront.domains.dateRange', 'Date Range')}
                </button>
            </div>

            {/* Filter bar */}
            <div className="flex flex-wrap items-center gap-3 rounded-xl border border-gray-200 bg-white px-4 py-3 dark:border-gray-700 dark:bg-gray-800">
                <button
                    type="button"
                    disabled
                    className="flex items-center gap-2 rounded-lg border border-gray-300 px-3 py-2 text-sm font-medium text-gray-500 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-600 dark:text-gray-400"
                    aria-label={t('storefront.domains.filterByAction', 'Filter by action')}
                >
                    {t('storefront.domains.filterByAction', 'Filter by action')}
                </button>
                <button
                    type="button"
                    disabled
                    className="flex items-center gap-2 rounded-lg border border-gray-300 px-3 py-2 text-sm font-medium text-gray-500 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-600 dark:text-gray-400"
                    aria-label={t('storefront.domains.filterByActor', 'Filter by actor')}
                >
                    {t('storefront.domains.filterByActor', 'Filter by actor')}
                </button>
            </div>

            {/* Loading */}
            {isLoading && (
                <div className="rounded-xl border border-gray-200 bg-white p-8 dark:border-gray-700 dark:bg-gray-800">
                    <AuthoringLoadingState />
                </div>
            )}

            {/* Error */}
            {isError && (
                <AuthoringFeedback
                    kind="error"
                    title={t('storefront.domains.historyError', 'Unable to load history.')}
                    message={error}
                    action={{
                        label: t('common.retry', 'Retry'),
                        onClick: () => refetch(),
                    }}
                />
            )}

            {/* Unauthorized */}
            {isUnauthorized && (
                <AuthoringFeedback
                    kind="error"
                    title={t('storefront.domains.unauthorized', 'Authentication required')}
                    message={t('storefront.domains.unauthorizedDesc', 'Please sign in to manage domains.')}
                />
            )}

            {/* Empty */}
            {isEmpty && entries.length === 0 && (
                <AuthoringEmptyState
                    title={t('storefront.domains.noHistory', 'No history entries')}
                    description={t(
                        'storefront.domains.noHistoryDesc',
                        'No audit events have been recorded for this domain.',
                    )}
                />
            )}

            {/* Content */}
            {isSuccess && entries.length > 0 && (
                <div className="overflow-x-auto rounded-xl border border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-800">
                    <table
                        className="w-full"
                        role="table"
                        aria-label={t('storefront.domains.history', 'History')}
                    >
                        <thead>
                            <tr className="border-b border-gray-200 bg-gray-50 dark:border-gray-700 dark:bg-gray-800/50">
                                {COLUMNS.map((col) => (
                                    <th
                                        key={col.key}
                                        className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-gray-400"
                                    >
                                        {t(col.labelKey, col.fallbackLabel)}
                                    </th>
                                ))}
                            </tr>
                        </thead>
                        <tbody>
                            {entries.map((entry) => (
                                <tr
                                    key={entry.id}
                                    className="border-b border-gray-200 last:border-0 dark:border-gray-700"
                                >
                                    {COLUMNS.map((col) => (
                                        <td
                                            key={col.key}
                                            className="px-4 py-3 text-sm text-gray-900 dark:text-gray-100"
                                        >
                                            {col.key === 'afterSnapshotJson'
                                                ? entry[col.key]
                                                    ? <pre className="max-w-xs overflow-x-auto whitespace-pre-wrap text-xs break-all">
                                                        {JSON.stringify(entry[col.key], null, 2)}
                                                      </pre>
                                                    : <span className="text-gray-400 dark:text-gray-500">—</span>
                                                : entry[col.key]}
                                        </td>
                                    ))}
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            {/* Pagination */}
            {data && data.totalCount > 0 && (
                <div className="flex items-center justify-between rounded-xl border border-gray-200 bg-white px-4 py-3 dark:border-gray-700 dark:bg-gray-800">
                    <span className="text-sm text-gray-500 dark:text-gray-400">
                        {t('storefront.domains.showingResults', 'Showing {{first}}-{{last}} of {{total}}', {
                            first: data.items.length > 0 ? (data.page - 1) * data.pageSize + 1 : 0,
                            last: data.items.length > 0 ? (data.page - 1) * data.pageSize + data.items.length : 0,
                            total: data.totalCount,
                        })}
                    </span>
                    <div className="flex items-center gap-2">
                        <button
                            type="button"
                            disabled={!data.hasPreviousPage || isLoading}
                            onClick={() => goToPage(data.page - 1)}
                            className="rounded-lg border border-gray-300 px-3 py-1 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-600 dark:text-gray-300"
                        >
                            {t('common.previous', 'Previous')}
                        </button>
                        <span className="text-sm text-gray-500 dark:text-gray-400">
                            {t('storefront.domains.page', 'Page {{current}} of {{total}}', {
                                current: data.page,
                                total: data.totalPages,
                            })}
                        </span>
                        <button
                            type="button"
                            disabled={!data.hasNextPage || isLoading}
                            onClick={() => goToPage(data.page + 1)}
                            className="rounded-lg border border-gray-300 px-3 py-1 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-600 dark:text-gray-300"
                        >
                            {t('common.next', 'Next')}
                        </button>
                    </div>
                </div>
            )}

            {/* Audit info */}
            <div className="rounded-xl border border-gray-200 bg-gray-50 p-6 dark:border-gray-700 dark:bg-gray-800/50">
                <div className="flex items-start gap-3">
                    <History size={20} className="text-gray-400 dark:text-gray-500 mt-0.5" />
                    <p className="text-sm text-gray-500 dark:text-gray-400">
                        {t(
                            'storefront.domains.historyInfo',
                            'Domain audit history from DomainAuditEntry — append-only log of all lifecycle transitions.',
                        )}
                    </p>
                </div>
            </div>
        </div>
    )
}

export default DomainHistoryPage

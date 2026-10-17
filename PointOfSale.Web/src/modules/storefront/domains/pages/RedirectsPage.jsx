/**
 * D07 — Redirects
 *
 * Constitutional source: DOMAIN-3.1 Canonical Blueprint §2.3 (C07), §2.4 (route: /storefront/domains/:id/redirects)
 *
 * Configures and manages redirect rules for a domain.
 *
 * DOMAIN-3.1-B: Backed by real READ backend data via useDomainRedirects hook.
 */

import { useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Plus, MoreVertical, ExternalLink } from 'lucide-react'

import {
    AuthoringButton,
    AuthoringEmptyState,
    AuthoringFeedback,
    AuthoringLoadingState,
    useAuthoringGuard,
} from '../../authoringPlatform'

import { useDomainRedirects } from '../hooks'

const COLUMNS = [
    { key: 'source', labelKey: 'storefront.domains.redirectSource', fallbackLabel: 'Source' },
    { key: 'target', labelKey: 'storefront.domains.redirectTarget', fallbackLabel: 'Target' },
    { key: 'type', labelKey: 'storefront.domains.redirectType', fallbackLabel: 'Type' },
    { key: 'status', labelKey: 'storefront.domains.status', fallbackLabel: 'Status' },
]

export function RedirectsPage() {
    const { t } = useTranslation()
    const { id } = useParams()

    const { data, error, isLoading, isError, isUnauthorized, isNotFound, isEmpty, isSuccess, refetch } =
        useDomainRedirects(id)

    const { requestNavigation } = useAuthoringGuard({ isDirty: false, isBusy: isLoading })

    const handleBack = () => {
        requestNavigation(`/storefront/domains/${id}`)
    }

    const redirects = data || []

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
                        {t('storefront.domains.redirects', 'Redirects')}
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
                    aria-label={t('storefront.domains.addRedirect', 'Add Redirect')}
                >
                    <Plus size={16} />
                    {t('storefront.domains.addRedirect', 'Add Redirect')}
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
                    title={t('storefront.domains.redirectsError', 'Redirects error')}
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
            {isEmpty && redirects.length === 0 && (
                <AuthoringEmptyState
                    title={t('storefront.domains.noRedirects', 'No redirects configured')}
                    description={t(
                        'storefront.domains.noRedirectsDesc',
                        'No redirect rules have been set up for this domain.',
                    )}
                />
            )}

            {/* Content */}
            {isSuccess && redirects.length > 0 && (
                <div className="overflow-x-auto rounded-xl border border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-800">
                    <table
                        className="w-full"
                        role="table"
                        aria-label={t('storefront.domains.redirects', 'Redirects')}
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
                                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase dark:text-gray-400">
                                    {t('storefront.domains.actions', 'Actions')}
                                </th>
                            </tr>
                        </thead>
                        <tbody>
                            {redirects.map((redirect) => (
                                <tr
                                    key={redirect.id}
                                    className="border-b border-gray-200 last:border-0 dark:border-gray-700"
                                >
                                    {COLUMNS.map((col) => (
                                        <td
                                            key={col.key}
                                            className="px-4 py-3 text-sm text-gray-900 dark:text-gray-100"
                                        >
                                            {redirect[col.key]}
                                        </td>
                                    ))}
                                    <td className="px-4 py-3 text-right">
                                        <button
                                            type="button"
                                            disabled
                                            className="rounded-lg border border-gray-300 p-1 text-gray-500 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-600 dark:text-gray-400"
                                            aria-label={t(
                                                'storefront.domains.rowActions',
                                                'Row actions for {{source}}',
                                                { source: redirect.source },
                                            )}
                                            title={t(
                                                'storefront.domains.rowActions',
                                                'Row actions for {{source}}',
                                                { source: redirect.source },
                                            )}
                                        >
                                            <MoreVertical size={16} />
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            {/* Redirect info */}
            <div className="rounded-xl border border-gray-200 bg-gray-50 p-6 dark:border-gray-700 dark:bg-gray-800/50">
                <div className="flex items-start gap-3">
                    <ExternalLink size={20} className="text-gray-400 dark:text-gray-500 mt-0.5" />
                    <div>
                        <h2 className="mb-2 text-lg font-semibold text-gray-900 dark:text-gray-100">
                            {t('storefront.domains.redirectSources', 'Redirect Sources')}
                        </h2>
                        <div className="space-y-2 text-sm text-gray-500 dark:text-gray-400">
                            <p>
                                {t(
                                    'storefront.domains.redirectSourcesDesc',
                                    'Domains redirecting to this Primary domain will appear here.',
                                )}
                            </p>
                            {redirects.length > 0 &&
                                redirects.map(
                                    (r) =>
                                        r.type !== 'None' && (
                                            <div key={r.id} className="flex items-center gap-2 text-xs">
                                                <ExternalLink size={12} />
                                                <span>{r.source} ← {r.target} ({r.type})</span>
                                            </div>
                                        ),
                                )}
                        </div>
                    </div>
                </div>
            </div>
        </div>
    )
}

export default RedirectsPage

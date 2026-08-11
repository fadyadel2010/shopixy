/**
 * D01 — Domains Overview
 *
 * Constitutional source: DOMAIN-3.1 Canonical Blueprint §2.3 (C01), §2.4 (route: /storefront/domains)
 *
 * Primary entry point for Domain Management. Provides a high-level summary of the
 * tenant's domain portfolio and quick actions.
 *
 * DOMAIN-3.1-B: Backed by real READ backend data via useDomainDashboard hook.
 */

import { useTranslation } from 'react-i18next'
import { Globe, Shield, Plus } from 'lucide-react'

import {
    AuthoringButton,
    AuthoringEmptyState,
    AuthoringFeedback,
    AuthoringLoadingState,
    AuthoringStatusBadge,
    useAuthoringGuard,
} from '../../authoringPlatform'

import { useDomainDashboard, LOAD_STATES } from '../hooks'

export function DomainsOverviewPage() {
    const { t } = useTranslation()
    const { data, state, error, isSuccess, isLoading, isEmpty, isError, isUnauthorized, refetch } =
        useDomainDashboard()

    const { requestNavigation } = useAuthoringGuard({
        isDirty: false,
        isBusy: isLoading,
    })

    const handleNavigate = (path) => {
        requestNavigation(path)
    }

    if (isLoading) {
        return <AuthoringLoadingState />
    }

    if (isUnauthorized) {
        return (
            <AuthoringFeedback
                kind="error"
                title={t('storefront.domains.unauthorized', 'Authentication required')}
                message={t('storefront.domains.unauthorizedDesc', 'Please sign in to manage domains.')}
            />
        )
    }

    if (isError) {
        return (
            <div className="space-y-4">
                <AuthoringFeedback
                    kind="error"
                    title={t('storefront.domains.loadError', 'Unable to load domains')}
                    message={error || t('storefront.domains.loadErrorDesc', 'Please try again.')}
                    action={{
                        label: t('common.retry', 'Retry'),
                        onClick: () => refetch(),
                    }}
                />
            </div>
        )
    }

    if (isEmpty || !data) {
        return (
            <div className="space-y-4">
                <AuthoringEmptyState
                    title={t('storefront.domains.noDomains', 'No domains yet')}
                    description={t(
                        'storefront.domains.noDomainsDesc',
                        'Add your first domain to get started.',
                    )}
                    action={{
                        label: t('storefront.domains.addDomain', 'Add Domain'),
                        onClick: () => handleNavigate('/storefront/domains/add'),
                    }}
                />
            </div>
        )
    }

    const summary = {
        totalDomains: data.totalDomains,
        sslExpiringSoon: data.sslExpiringSoon,
        pendingVerification: data.pendingVerification,
        draft: data.draftCount,
    }

    const primaryDomain = data.primaryDomain
    const recentDomains = data.recentDomains || []

    return (
        <div className="space-y-4">
            <AuthoringFeedback
                kind="info"
                title={t('storefront.domains.summaryHeader', 'You have {{count}} domains connected', {
                    count: summary.totalDomains,
                })}
                message={t(
                    'storefront.domains.summarySubtext',
                    'Manage your custom domains, DNS verification, SSL certificates, and redirects.',
                )}
            />

            {/* Summary Cards */}
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
                {/* Primary Domain Card */}
                <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
                    <div className="mb-3 flex h-10 w-10 items-center justify-center rounded-lg bg-indigo-100 dark:bg-indigo-900/30">
                        <Globe size={20} className="text-indigo-600 dark:text-indigo-400" />
                    </div>
                    <h3 className="text-sm font-medium text-gray-500 dark:text-gray-400">
                        {t('storefront.domains.primaryDomain', 'Primary Domain')}
                    </h3>
                    {primaryDomain ? (
                        <>
                            <p className="mt-2 text-lg font-semibold text-gray-900 dark:text-gray-100">
                                {primaryDomain.hostName}
                            </p>
                            <div className="mt-2 flex items-center gap-2">
                                <AuthoringStatusBadge state="live">
                                    {t('storefront.domains.primary', 'Primary')}
                                </AuthoringStatusBadge>
                            </div>
                            <p className="mt-2 text-sm text-gray-500 dark:text-gray-400">
                                {t('storefront.domains.canonicalUrl', 'Canonical URL')}:{' '}
                                <span className="font-mono text-xs text-gray-600 dark:text-gray-400 break-all">
                                    {' '}
                                    {primaryDomain.canonicalUrl || `https://${primaryDomain.hostName}`}
                                </span>
                            </p>
                        </>
                    ) : (
                        <p className="mt-2 text-sm text-gray-500 dark:text-gray-400">
                            {t('storefront.domains.noPrimaryDomain', 'No primary domain configured')}
                        </p>
                    )}
                </div>

                {/* Total Domains Card */}
                <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
                    <div className="mb-3 flex h-10 w-10 items-center justify-center rounded-lg bg-blue-100 dark:bg-blue-900/30">
                        <Globe size={20} className="text-blue-600 dark:text-blue-400" />
                    </div>
                    <h3 className="text-sm font-medium text-gray-500 dark:text-gray-400">
                        {t('storefront.domains.totalDomains', 'Total Domains')}
                    </h3>
                    <p className="mt-2 text-3xl font-bold text-gray-900 dark:text-gray-100">
                        {summary.totalDomains}
                    </p>
                    <div className="mt-2 space-y-1 text-xs text-gray-500 dark:text-gray-400">
                        <p>{summary.pendingVerification} {t('storefront.domains.pending', 'Pending')}</p>
                        <p>{summary.draft} {t('storefront.domains.draft', 'Draft')}</p>
                    </div>
                </div>

                {/* SSL Health Card */}
                <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
                    <div className="mb-3 flex h-10 w-10 items-center justify-center rounded-lg bg-amber-100 dark:bg-amber-900/30">
                        <Shield size={20} className="text-amber-600 dark:text-amber-400" />
                    </div>
                    <h3 className="text-sm font-medium text-gray-500 dark:text-gray-400">
                        {t('storefront.domains.sslHealth', 'SSL Health')}
                    </h3>
                    <p className="mt-2 text-3xl font-bold text-gray-900 dark:text-gray-100">
                        {summary.sslExpiringSoon}
                    </p>
                    <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                        {t('storefront.domains.expiringSoon', 'expiring in 30 days')}
                    </p>
                </div>

                {/* Quick Actions Card */}
                <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
                    <div className="mb-3 flex h-10 w-10 items-center justify-center rounded-lg bg-emerald-100 dark:bg-emerald-900/30">
                        <Plus size={20} className="text-emerald-600 dark:text-emerald-400" />
                    </div>
                    <h3 className="text-sm font-medium text-gray-500 dark:text-gray-400">
                        {t('storefront.domains.quickActions', 'Quick Actions')}
                    </h3>
                    <div className="mt-3 space-y-2">
                        <AuthoringButton
                            action={{
                                label: t('storefront.domains.addDomain', 'Add Domain'),
                                icon: Plus,
                                onClick: () => handleNavigate('/storefront/domains/add'),
                                tone: 'primary',
                            }}
                            defaultLabel={t('storefront.domains.addDomain', 'Add Domain')}
                            tone="primary"
                        />
                        <AuthoringButton
                            action={{
                                label: t('storefront.domains.renewSsl', 'Renew SSL'),
                                onClick: () => {},
                            }}
                            defaultLabel={t('storefront.domains.renewSsl', 'Renew SSL')}
                        />
                        <AuthoringButton
                            action={{
                                label: t('storefront.domains.manageRedirects', 'Manage Redirects'),
                                onClick: () => {},
                            }}
                            defaultLabel={t('storefront.domains.manageRedirects', 'Manage Redirects')}
                        />
                    </div>
                </div>
            </div>

            {/* Recent Domains */}
            <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
                <div className="mb-4 flex items-center justify-between">
                    <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
                        {t('storefront.domains.recentDomains', 'Recent Domains')}
                    </h2>
                    <AuthoringButton
                        action={{
                            label: t('storefront.domains.viewAll', 'View All Domains'),
                            onClick: () => handleNavigate('/storefront/domains/list'),
                        }}
                        defaultLabel={t('storefront.domains.viewAll', 'View All Domains')}
                        tone="primary"
                    />
                </div>
                {recentDomains.length === 0 ? (
                    <AuthoringEmptyState
                        title={t('storefront.domains.noRecentDomains', 'No recent domains')}
                        description={t(
                            'storefront.domains.noRecentDomainsDesc',
                            'Add a domain to see it listed here.',
                        )}
                    />
                ) : (
                    <div className="overflow-x-auto">
                        <table
                            className="w-full"
                            role="table"
                            aria-label={t('storefront.domains.recentDomains', 'Recent Domains')}
                        >
                            <thead>
                                <tr>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-gray-400">
                                        {t('storefront.domains.hostname', 'Hostname')}
                                    </th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-gray-400">
                                        {t('storefront.domains.type', 'Type')}
                                    </th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-gray-400">
                                        {t('storefront.domains.status', 'Status')}
                                    </th>
                                    <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase dark:text-gray-400">
                                        {t('storefront.domains.actions', 'Actions')}
                                    </th>
                                </tr>
                            </thead>
                            <tbody>
                                {recentDomains.map((domain) => (
                                    <tr
                                        key={domain.id ?? domain.hostName}
                                        className="border-b border-gray-200 last:border-0 dark:border-gray-700"
                                    >
                                        <td className="px-4 py-3 text-sm text-gray-900 dark:text-gray-100">
                                            <button
                                                type="button"
                                                onClick={() =>
                                                    handleNavigate(`/storefront/domains/${domain.id ?? domain.hostName}`)
                                                }
                                                className="font-mono text-xs text-indigo-600 hover:text-indigo-800 dark:text-indigo-400 dark:hover:text-indigo-300"
                                            >
                                                {domain.hostName}
                                            </button>
                                        </td>
                                        <td className="px-4 py-3 text-sm text-gray-600 dark:text-gray-300">
                                            {domain.domainType}
                                        </td>
                                        <td className="px-4 py-3 text-sm">
                                            <AuthoringStatusBadge
                                                state={
                                                    domain.lifecycleState === 'Primary'
                                                        ? 'live'
                                                        : domain.isVerified
                                                          ? 'published'
                                                          : 'draft'
                                                }
                                            >
                                                {domain.lifecycleState}
                                            </AuthoringStatusBadge>
                                        </td>
                                        <td className="px-4 py-3 text-right">
                                            <button
                                                type="button"
                                                disabled
                                                className="rounded-lg border border-gray-300 p-1 text-gray-500 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-600 dark:text-gray-400"
                                                aria-label={t('storefront.domains.actions', 'Actions')}
                                                title={t('storefront.domains.actions', 'Actions')}
                                            >
                                                <Plus size={16} />
                                            </button>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>
        </div>
    )
}

export default DomainsOverviewPage

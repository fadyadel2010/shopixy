/**
 * D02 — Domain Detail
 *
 * Constitutional source: DOMAIN-3.1 Canonical Blueprint §2.3 (C02), §2.4 (route: /storefront/domains/:id)
 *
 * Detailed view of a single domain with tabbed navigation covering
 * Overview, Verification, SSL, Redirects, and History.
 *
 * DOMAIN-3.1-B: Backed by real READ backend data via useDomainDetail hook.
 * DOMAIN-3.1-C: WRITE path actions wired to useDomainMutations hook.
 */

import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Globe, Archive, RotateCcw, Trash2, Save, Shield } from 'lucide-react'

import {
    AuthoringButton,
    AuthoringEmptyState,
    AuthoringFeedback,
    AuthoringLoadingState,
    AuthoringStatusBadge,
    AuthoringConfirmDialog,
    useAuthoringGuard,
} from '../../authoringPlatform'

import { useDomainDetail, useDomainMutations, LOAD_STATES } from '../hooks'

const TABS = [
    { id: 'overview', labelKey: 'storefront.ia.domainOverview', fallbackLabel: 'Overview' },
    { id: 'verification', labelKey: 'storefront.ia.dnsVerification', fallbackLabel: 'Verification' },
    { id: 'ssl', labelKey: 'storefront.ia.sslManagement', fallbackLabel: 'SSL' },
    { id: 'redirects', labelKey: 'storefront.ia.redirects', fallbackLabel: 'Redirects' },
    { id: 'history', labelKey: 'storefront.ia.domainHistory', fallbackLabel: 'History' },
]

export function DomainDetailPage() {
    const { t } = useTranslation()
    const { id } = useParams()
    const [activeTab, setActiveTab] = useState('overview')
    const [dialogState, setDialogState] = useState({ isOpen: false, action: null, domain: null })

    const { data, state, error, isLoading, isError, isUnauthorized, isSuccess, refetch } =
        useDomainDetail(id)

    const { requestNavigation } = useAuthoringGuard({
        isDirty: false,
        isBusy: isLoading,
    })

    const {
        isMutating,
        error: mutationError,
        errorCode: mutationErrorCode,
        isConcurrencyConflict,
        serverVersion,
        archiveDomain,
        restoreDomain,
        deleteDomain,
        promotePrimaryDomain,
        startDomainVerification,
        clearError,
    } = useDomainMutations({
        mutationOptions: {
            onSuccess: (result) => {
                // Close dialog and refetch detail
                setDialogState({ isOpen: false, action: null, domain: null })
                refetch()
            },
            onError: () => {
                // Error is captured in hook state; dialog stays open for retry
            },
        },
    })

    const handleNavigate = (path) => {
        requestNavigation(path)
    }

    const handleTabClick = (tabId) => {
        const routeMap = {
            overview: `/storefront/domains/${id}`,
            verification: `/storefront/domains/${id}/verification`,
            ssl: `/storefront/domains/${id}/ssl`,
            redirects: `/storefront/domains/${id}/redirects`,
            history: `/storefront/domains/${id}/history`,
        }
        if (routeMap[tabId]) {
            handleNavigate(routeMap[tabId])
        }
    }

    const handleTabChange = (tabId) => {
        setActiveTab(tabId)
        handleTabClick(tabId)
    }

    // --- Action handlers with domain lifecycle awareness ---

    const canArchive = data && !data.isPrimary && data.lifecycleState !== 'Archived' &&
                        data.lifecycleState !== 'Deleted'

    const canRestore = data && (data.lifecycleState === 'Deleted' ||
                                 data.lifecycleState === 'Archived' ||
                                 data.lifecycleState === 'Disabled')

    const canDelete = data && !data.isPrimary &&
                       data.lifecycleState !== 'Deleted' &&
                       data.lifecycleState !== 'Archived'

    const canPromote = data && !data.isPrimary &&
                        (data.lifecycleState === 'SslActive' || data.lifecycleState === 'Verified')
    const canStartVerification = data && !data.isPrimary && data.lifecycleState === 'Draft'

    const handleStartVerification = async () => {
        if (!data || isMutating) return
        try {
            await startDomainVerification(data.id, { expectedVersion: data.version })
            requestNavigation(`/storefront/domains/${data.id}/verification`)
        } catch {
            // Error state is managed by the hook.
        }
    }

    const openDialog = (action, domainData) => {
        clearError()
        setDialogState({ isOpen: true, action, domain: domainData })
    }

    const closeDialog = () => {
        setDialogState({ isOpen: false, action: null, domain: null })
        clearError()
    }

    const confirmAction = async () => {
        const domainId = data.id
        const version = data.version

        try {
            switch (dialogState.action) {
                case 'archive':
                    await archiveDomain(domainId, { reason: 'Archived via domain management UI', expectedVersion: version })
                    break
                case 'restore':
                    await restoreDomain(domainId, { reason: 'Restored via domain management UI', expectedVersion: version })
                    break
                case 'delete':
                    await deleteDomain(domainId, { reason: 'Deleted via domain management UI', expectedVersion: version })
                    break
                case 'promote':
                    await promotePrimaryDomain(domainId, { expectedVersion: version })
                    break
            }
            // onSuccess callback in useDomainMutations will close dialog and refetch
        } catch (err) {
            // Error state is managed by the hook
        }
    }

    const getDialogConfig = () => {
        const { action, domain } = dialogState
        const hostName = domain?.hostName || id

        switch (action) {
            case 'archive':
                return {
                    title: t('storefront.domains.archiveDomain', 'Archive Domain'),
                    description: t(
                        'storefront.domains.archiveDomainDesc',
                        'Are you sure you want to archive {{host}}? Archived domains are inactive but can be restored.',
                        { host: hostName },
                    ),
                    confirmLabel: t('storefront.domains.archive', 'Archive'),
                    cancelLabel: t('common.cancel', 'Cancel'),
                    destructive: true,
                }
            case 'restore':
                return {
                    title: t('storefront.domains.restoreDomain', 'Restore Domain'),
                    description: t(
                        'storefront.domains.restoreDomainDesc',
                        'Are you sure you want to restore {{host}}? The domain will be returned to Draft state.',
                        { host: hostName },
                    ),
                    confirmLabel: t('storefront.domains.restore', 'Restore'),
                    cancelLabel: t('common.cancel', 'Cancel'),
                    destructive: false,
                }
            case 'delete':
                return {
                    title: t('storefront.domains.deleteDomain', 'Delete Domain'),
                    description: t(
                        'storefront.domains.deleteDomainDesc',
                        'Are you sure you want to delete {{host}}? This is a soft delete — the domain record is retained for audit purposes. It can be restored within 90 days.',
                        { host: hostName },
                    ),
                    confirmLabel: t('storefront.domains.delete', 'Delete'),
                    cancelLabel: t('common.cancel', 'Cancel'),
                    destructive: true,
                }
            case 'promote':
                return {
                    title: t('storefront.domains.promotePrimary', 'Promote to Primary'),
                    description: t(
                        'storefront.domains.promotePrimaryDesc',
                        'Are you sure you want to make {{host}} the primary domain? This will atomically demote the current primary domain. Exactly one primary domain is guaranteed per tenant.',
                        { host: hostName },
                    ),
                    confirmLabel: t('storefront.domains.promote', 'Promote'),
                    cancelLabel: t('common.cancel', 'Cancel'),
                    destructive: false,
                }
            default:
                return null
        }
    }

    if (isLoading) {
        return <AuthoringLoadingState />
    }

    if (isUnauthorized) {
        return (
            <div className="space-y-4">
                <AuthoringButton
                    action={{
                        label: t('common.back', 'Back'),
                        onClick: () => handleNavigate('/storefront/domains'),
                    }}
                    defaultLabel={t('common.back', 'Back')}
                />
                <AuthoringFeedback
                    kind="error"
                    title={t('storefront.domains.unauthorized', 'Authentication required')}
                    message={t('storefront.domains.unauthorizedDesc', 'Please sign in to manage domains.')}
                />
            </div>
        )
    }

    if (isError) {
        return (
            <div className="space-y-4">
                <AuthoringButton
                    action={{
                        label: t('common.back', 'Back'),
                        onClick: () => handleNavigate('/storefront/domains'),
                    }}
                    defaultLabel={t('common.back', 'Back')}
                />
                <AuthoringFeedback
                    kind="error"
                    title={t('storefront.domains.loadError', 'Unable to load domain')}
                    message={error}
                    action={{
                        label: t('common.retry', 'Retry'),
                        onClick: () => refetch(),
                    }}
                />
            </div>
        )
    }

    if (state === LOAD_STATES.NOT_FOUND) {
        return (
            <div className="space-y-4">
                <AuthoringButton
                    action={{
                        label: t('common.back', 'Back'),
                        onClick: () => handleNavigate('/storefront/domains'),
                    }}
                    defaultLabel={t('common.back', 'Back')}
                />
                <AuthoringEmptyState
                    title={t('storefront.domains.domainNotFound', 'Domain not found')}
                    description={t(
                        'storefront.domains.domainNotFoundDesc',
                        'The requested domain could not be found.',
                    )}
                />
            </div>
        )
    }

    if (!data) {
        return (
            <div className="space-y-4">
                <AuthoringButton
                    action={{
                        label: t('common.back', 'Back'),
                        onClick: () => handleNavigate('/storefront/domains'),
                    }}
                    defaultLabel={t('common.back', 'Back')}
                />
                <AuthoringEmptyState
                    title={t('storefront.domains.domainNotFound', 'Domain not found')}
                    description={t(
                        'storefront.domains.domainNotFoundDesc',
                        'The requested domain could not be found.',
                    )}
                />
            </div>
        )
    }

    const hostname = data.hostName || id

    return (
        <div className="space-y-4">
            <AuthoringButton
                action={{
                    label: t('common.back', 'Back'),
                    onClick: () => handleNavigate('/storefront/domains'),
                }}
                defaultLabel={t('common.back', 'Back')}
            />

            {/* Domain Header */}
            <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
                <div className="mb-4 flex items-center gap-3">
                    <Globe size={24} className="text-indigo-600 dark:text-indigo-400" />
                    <h1 className="text-xl font-semibold text-gray-900 dark:text-gray-100">
                        {t('storefront.ia.domainDetail', 'Domain Detail')}
                    </h1>
                </div>
                <div className="flex items-center gap-4 flex-wrap">
                    <span className="text-sm text-gray-500 dark:text-gray-400">
                        {t('storefront.domains.hostname', 'Hostname')}:{' '}
                    </span>
                    <span className="font-mono text-sm text-gray-900 dark:text-gray-100 break-all">
                        {hostname}
                    </span>
                    <AuthoringStatusBadge state={data.isVerified ? 'published' : 'draft'}>
                        {data.isVerified
                            ? t('storefront.domains.verified', 'Verified')
                            : t('storefront.domains.notVerified', 'Not Verified')}
                    </AuthoringStatusBadge>

                    {/* Action buttons — DOMAIN-3.1-C */}
                    <div className="ml-auto flex items-center gap-2 mt-2 sm:mt-0">
                        {canStartVerification && (
                            <button
                                type="button"
                                onClick={handleStartVerification}
                                disabled={isMutating}
                                className="flex items-center gap-2 rounded-lg border border-indigo-500 bg-indigo-600 px-3 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
                            >
                                <Shield size={16} />
                                {t('storefront.domains.startVerification', 'Start Verification')}
                            </button>
                        )}
                        {canPromote && (
                            <button
                                type="button"
                                onClick={() => openDialog('promote', data)}
                                disabled={isMutating}
                                className="flex items-center gap-2 rounded-lg border border-indigo-500 bg-indigo-50 px-3 py-2 text-sm font-medium text-indigo-700 hover:bg-indigo-100 disabled:opacity-50 dark:border-indigo-400 dark:bg-indigo-900/20 dark:text-indigo-300"
                            >
                                <Shield size={16} />
                                {t('storefront.domains.promotePrimary', 'Promote Primary')}
                            </button>
                        )}
                        {canArchive && (
                            <button
                                type="button"
                                onClick={() => openDialog('archive', data)}
                                disabled={isMutating}
                                className="flex items-center gap-2 rounded-lg border border-gray-300 px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50 dark:border-gray-600 dark:text-gray-300"
                            >
                                <Archive size={16} />
                                {t('storefront.domains.archive', 'Archive')}
                            </button>
                        )}
                        {canRestore && (
                            <button
                                type="button"
                                onClick={() => openDialog('restore', data)}
                                disabled={isMutating}
                                className="flex items-center gap-2 rounded-lg border border-gray-300 px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50 dark:border-gray-600 dark:text-gray-300"
                            >
                                <RotateCcw size={16} />
                                {t('storefront.domains.restore', 'Restore')}
                            </button>
                        )}
                        {canDelete && (
                            <button
                                type="button"
                                onClick={() => openDialog('delete', data)}
                                disabled={isMutating}
                                className="flex items-center gap-2 rounded-lg border border-red-300 bg-red-50 px-3 py-2 text-sm font-medium text-red-700 hover:bg-red-100 disabled:opacity-50 dark:border-red-600 dark:bg-red-900/20 dark:text-red-300"
                            >
                                <Trash2 size={16} />
                                {t('storefront.domains.delete', 'Delete')}
                            </button>
                        )}
                    </div>

                    {/* Mutation error display */}
                    {mutationError && (
                        <AuthoringFeedback
                            kind="error"
                            title={t('storefront.domains.actionError', 'Action failed')}
                            message={mutationError}
                        />
                    )}

                    {isConcurrencyConflict && (
                        <AuthoringFeedback
                            kind="warning"
                            title={t('storefront.domains.concurrencyConflict', 'Concurrent Modification')}
                            message={t(
                                'storefront.domains.concurrencyConflictDesc',
                                'This domain was modified by another user. Please refresh and try again.',
                            )}
                        />
                    )}
                </div>
            </div>

            {/* Tab navigation */}
            <div className="border-b border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-800">
                <nav role="tablist" aria-label="Domain detail tabs" className="flex -mb-px overflow-x-auto">
                    {TABS.map((tab) => {
                        const isActive = activeTab === tab.id
                        return (
                            <button
                                key={tab.id}
                                type="button"
                                role="tab"
                                aria-selected={isActive}
                                aria-controls={`domain-tab-panel-${tab.id}`}
                                id={`domain-tab-${tab.id}`}
                                onClick={() => handleTabChange(tab.id)}
                                className={`flex-shrink-0 border-b-2 px-4 py-3 text-sm font-medium whitespace-nowrap focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 ${
                                    isActive
                                        ? 'border-indigo-600 text-indigo-600'
                                        : 'border-transparent text-gray-600 hover:border-gray-300 hover:text-gray-800 dark:text-gray-300 dark:hover:border-gray-600'
                                }`}
                            >
                                {t(tab.labelKey, tab.fallbackLabel)}
                            </button>
                        )
                    })}
                </nav>
            </div>

            {/* Tab panels */}
            <div className="space-y-4">
                {TABS.map((tab) => {
                    const isActive = activeTab === tab.id
                    return (
                        <div
                            key={tab.id}
                            id={`domain-tab-panel-${tab.id}`}
                            role="tabpanel"
                            aria-labelledby={`domain-tab-${tab.id}`}
                            tabIndex={0}
                            hidden={!isActive}
                        >
                            {isActive && <TabContent tabId={tab.id} data={data} t={t} onNavigate={handleNavigate} />}
                        </div>
                    )
                })}
            </div>

            {/* Confirmation Dialog — DOMAIN-3.1-C */}
            {dialogState.isOpen && getDialogConfig() && (
                <AuthoringConfirmDialog
                    open={dialogState.isOpen}
                    title={getDialogConfig().title}
                    description={getDialogConfig().description}
                    confirmLabel={getDialogConfig().confirmLabel}
                    confirmTone={getDialogConfig().destructive ? 'danger' : 'primary'}
                    busy={isMutating}
                    onConfirm={confirmAction}
                    onClose={closeDialog}
                />
            )}
        </div>
    )
}

function TabContent({ tabId, data, t, onNavigate }) {
    switch (tabId) {
        case 'overview':
            return (
                <div className="space-y-4">
                    <div className="rounded-lg border border-gray-200 bg-gray-50 p-4 dark:border-gray-700 dark:bg-gray-700/50">
                        <span className="text-xs font-medium text-gray-500 dark:text-gray-400">
                            {data.hostName}
                        </span>
                        <p className="mt-1 font-mono text-sm text-gray-900 dark:text-gray-100 break-all">
                            {data.canonicalUrl || `https://${data.hostName}`}
                        </p>
                    </div>
                    <div className="rounded-lg border border-gray-200 bg-gray-50 p-4 dark:border-gray-700 dark:bg-gray-700/50">
                        <span className="text-xs font-medium text-gray-500 dark:text-gray-400">
                            {t('storefront.domains.domainType', 'Domain Type')}
                        </span>
                        <p className="mt-1 text-sm text-gray-900 dark:text-gray-100">{data.domainType}</p>
                    </div>
                    <div className="rounded-lg border border-gray-200 bg-gray-50 p-4 dark:border-gray-700 dark:bg-gray-700/50">
                        <span className="text-xs font-medium text-gray-500 dark:text-gray-400">
                            {t('storefront.domains.lifecycleState', 'Lifecycle State')}
                        </span>
                        <p className="mt-1 text-sm text-gray-900 dark:text-gray-100">{data.lifecycleState}</p>
                    </div>
                    <div className="rounded-lg border border-gray-200 bg-gray-50 p-4 dark:border-gray-700 dark:bg-gray-700/50">
                        <span className="text-xs font-medium text-gray-500 dark:text-gray-400">
                            {t('storefront.domains.redirectType', 'Redirect Type')}
                        </span>
                        <p className="mt-1 text-sm text-gray-900 dark:text-gray-100">{data.redirectType}</p>
                    </div>
                    {data.redirectTargetHost && (
                        <div className="rounded-lg border border-gray-200 bg-gray-50 p-4 dark:border-gray-700 dark:bg-gray-700/50">
                            <span className="text-xs font-medium text-gray-500 dark:text-gray-400">
                                {t('storefront.domains.redirectTarget', 'Redirect Target')}
                            </span>
                            <p className="mt-1 text-sm text-gray-900 dark:text-gray-100 break-all">
                                {data.redirectTargetHost}
                            </p>
                        </div>
                    )}
                </div>
            )
        case 'verification':
            return (
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-6 text-center dark:border-gray-700 dark:bg-gray-700/50">
                    <p className="text-sm text-gray-500 dark:text-gray-400">
                        {t('storefront.domains.verificationNavigate', 'DNS verification — navigate to /verification for full details.')}
                    </p>
                </div>
            )
        case 'ssl':
            return <SslSummaryPanel data={data} t={t} onNavigate={onNavigate} />
            return (
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-6 text-center dark:border-gray-700 dark:bg-gray-700/50">
                    <p className="text-sm text-gray-500 dark:text-gray-400">
                        {t('storefront.domains.sslNavigate', 'SSL management — navigate to /ssl for full details.')}
                    </p>
                </div>
            )
        case 'redirects':
            return (
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-6 text-center dark:border-gray-700 dark:bg-gray-700/50">
                    <p className="text-sm text-gray-500 dark:text-gray-400">
                        {t('storefront.domains.redirectsNavigate', 'Redirects — navigate to /redirects for full details.')}
                    </p>
                </div>
            )
        case 'history':
            return (
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-6 text-center dark:border-gray-700 dark:bg-gray-700/50">
                    <p className="text-sm text-gray-500 dark:text-gray-400">
                        {t('storefront.domains.historyNavigate', 'Domain audit history — navigate to /history for the full log.')}
                    </p>
                </div>
            )
        default:
            return (
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-6 text-center dark:border-gray-700 dark:bg-gray-700/50">
                    <p className="text-sm text-gray-500 dark:text-gray-400">{t('storefront.domains.noContent', 'No content available.')}</p>
                </div>
            )
    }
}

function SslSummaryPanel({ data, t, onNavigate }) {
    return (
        <div className="rounded-lg border border-gray-200 bg-gray-50 p-4 dark:border-gray-700 dark:bg-gray-700/50">
            <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                        <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                            {data.sslCertificateId
                                ? t('storefront.domains.sslSummary.configured', 'SSL configured')
                                : data.isVerified
                                  ? t('storefront.domains.sslSummary.setupRequired', 'SSL setup required')
                                  : t('storefront.domains.sslSummary.verifyFirst', 'Verify domain before SSL')}
                        </p>
                        {data.isPrimary && (
                            <AuthoringStatusBadge state="live">
                                {t('storefront.domains.ssl.primaryDomain', 'Primary Domain')}
                            </AuthoringStatusBadge>
                        )}
                    </div>
                    <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
                        {data.sslActivatedAtUtc
                            ? t('storefront.domains.sslSummary.activatedAt', 'Certificate activated at {{date}}.', {
                                  date: data.sslActivatedAtUtc,
                              })
                            : data.sslCertificateId
                              ? t('storefront.domains.sslSummary.requiresAttention', 'Open SSL Management to review current certificate state.')
                              : data.isVerified
                                ? t('storefront.domains.sslSummary.provisionNext', 'Provision a certificate from SSL Management.')
                                : t('storefront.domains.sslSummary.verificationNext', 'Complete DNS verification before requesting a certificate.')}
                    </p>
                </div>
                <AuthoringButton
                    action={{
                        label: t('storefront.domains.sslSummary.open', 'Open SSL Management'),
                        onClick: () => onNavigate(`/storefront/domains/${data.id}/ssl`),
                    }}
                    defaultLabel={t('storefront.domains.sslSummary.open', 'Open SSL Management')}
                    tone="primary"
                />
            </div>
        </div>
    )
}

export default DomainDetailPage

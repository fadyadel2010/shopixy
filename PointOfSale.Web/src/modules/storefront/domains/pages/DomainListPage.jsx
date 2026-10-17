/**
 * D04 — Domain List
 *
 * Constitutional source: DOMAIN-3.1 Canonical Blueprint §2.3 (C04), §2.4 (route: /storefront/domains/list)
 *
 * Paginated data table of all domains for the tenant. Columns, sorting, filtering,
 * and search backed by real READ backend data.
 *
 * DOMAIN-3.1-B: Backed by real READ backend data via useDomainList hook.
 */

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Search, Filter, ArrowUpDown, MoreVertical, Archive, RotateCcw, Trash2, Shield } from 'lucide-react'

import {
    AuthoringButton,
    AuthoringEmptyState,
    AuthoringFeedback,
    AuthoringLoadingState,
    AuthoringConfirmDialog,
    useAuthoringGuard,
} from '../../authoringPlatform'

import { useDomainList, useDomainMutations } from '../hooks'

export function DomainListPage() {
    const { t } = useTranslation()
    const [searchTerm, setSearchTerm] = useState('')
    const [dropdownOpenId, setDropdownOpenId] = useState(null)
    const [confirmDialog, setConfirmDialog] = useState({ open: false, action: null, domain: null })

    const {
        data,
        params,
        error,
        isLoading,
        isEmpty,
        isError,
        isUnauthorized,
        isSuccess,
        setSearch,
        setSort,
        goToPage,
        refetch,
    } = useDomainList({ search: searchTerm })

    const { requestNavigation } = useAuthoringGuard({ isDirty: false, isBusy: isLoading })

    const {
        archiveDomain,
        restoreDomain,
        deleteDomain,
        promotePrimaryDomain,
        startDomainVerification,
        isMutating,
        mutationError,
    } = useDomainMutations()

    const handleNavigate = (path) => {
        requestNavigation(path)
    }

    const handleSearch = (e) => {
        const val = e.target.value
        setSearchTerm(val)
        setSearch(val)
    }

    const handleSort = (key) => {
        const currentSortDir = params.sortBy === key && params.sortDir === 'asc' ? 'desc' : 'asc'
        setSort(key, currentSortDir)
    }

    // --- Domain action handlers (DOMAIN-3.1-C) ---

    const getAvailableActions = (domain) => {
        const actions = []
        const isDeleted = domain.lifecycleState === 'Deleted'
        const isArchived = domain.lifecycleState === 'Archived'
        const isPrimary = domain.isPrimary
        const canRestore = isDeleted || isArchived || domain.lifecycleState === 'Disabled'
        const canArchive = !isDeleted && !isArchived && !isPrimary
        const canDelete = !isDeleted && !isArchived && !isPrimary
        const canPromote = !isPrimary &&
            domain.lifecycleState === 'SslActive'
        const canStartVerification = !isPrimary && domain.lifecycleState === 'Draft'

        if (canStartVerification) actions.push('startVerification')
        if (canPromote) actions.push('promote')
        if (canArchive) actions.push('archive')
        if (canRestore) actions.push('restore')
        if (canDelete) actions.push('delete')

        return actions
    }

    const openActionMenu = (domainId) => {
        setDropdownOpenId(dropdownOpenId === domainId ? null : domainId)
    }

    const handleAction = (action, domain) => {
        setDropdownOpenId(null)
        setConfirmDialog({ open: true, action, domain })
    }

    const handleConfirmAction = async () => {
        const { action, domain } = confirmDialog
        const { id, version } = domain

        try {
            switch (action) {
                case 'archive':
                    await archiveDomain(id, { reason: 'Archived via domain list UI', expectedVersion: version })
                    break
                case 'restore':
                    await restoreDomain(id, { reason: 'Restored via domain list UI', expectedVersion: version })
                    break
                case 'delete':
                    await deleteDomain(id, { reason: 'Deleted via domain list UI', expectedVersion: version })
                    break
                case 'promote':
                    await promotePrimaryDomain(id, { expectedVersion: version })
                    break
                case 'startVerification':
                    await startDomainVerification(id, { expectedVersion: version })
                    break
            }
            await refetch()
            setConfirmDialog({ open: false, action: null, domain: null })
        } catch {
            // The hook renders the error. Retain confirmation for retry and
            // consume the event handler's rejected promise.
        }
    }

    const getActionDialogConfig = () => {
        const { action, domain } = confirmDialog
        const hostName = domain?.hostName || ''

        switch (action) {
            case 'archive':
                return {
                    title: t('storefront.domains.archiveDomain', 'Archive Domain'),
                    description: t(
                        'storefront.domains.archiveDomainDesc',
                        'Are you sure you want to archive {{host}}?',
                        { host: hostName },
                    ),
                    confirmLabel: t('storefront.domains.archive', 'Archive'),
                    confirmTone: 'secondary',
                    destructive: true,
                }
            case 'restore':
                return {
                    title: t('storefront.domains.restoreDomain', 'Restore Domain'),
                    description: t(
                        'storefront.domains.restoreDomainDesc',
                        'Are you sure you want to restore {{host}}?',
                        { host: hostName },
                    ),
                    confirmLabel: t('storefront.domains.restore', 'Restore'),
                    confirmTone: 'primary',
                    destructive: false,
                }
            case 'delete':
                return {
                    title: t('storefront.domains.deleteDomain', 'Delete Domain'),
                    description: t(
                        'storefront.domains.deleteDomainDesc',
                        'Are you sure you want to delete {{host}}? This is a soft delete.',
                        { host: hostName },
                    ),
                    confirmLabel: t('storefront.domains.delete', 'Delete'),
                    confirmTone: 'danger',
                    destructive: true,
                }
            case 'promote':
                return {
                    title: t('storefront.domains.promotePrimary', 'Promote to Primary'),
                    description: t(
                        'storefront.domains.promotePrimaryDesc',
                        'Make {{host}} the primary domain? The current primary will be demoted.',
                        { host: hostName },
                    ),
                    confirmLabel: t('storefront.domains.promote', 'Promote'),
                    confirmTone: 'primary',
                    destructive: false,
                }
            case 'startVerification':
                return {
                    title: t('storefront.domains.startVerification', 'Start Verification'),
                    description: t(
                        'storefront.domains.startVerificationDesc',
                        'Start DNS verification for {{host}}?',
                        { host: hostName },
                    ),
                    confirmLabel: t('storefront.domains.startVerification', 'Start Verification'),
                    confirmTone: 'primary',
                    destructive: false,
                }
            default:
                return null
        }
    }

    const domains = data?.items || []

    const columns = [
        { key: 'hostName', sortKey: 'hostname', labelKey: 'storefront.domains.hostname', fallbackLabel: 'Domain' },
        { key: 'domainType', sortKey: 'type', labelKey: 'storefront.domains.type', fallbackLabel: 'Type' },
        { key: 'lifecycleState', sortKey: 'status', labelKey: 'storefront.domains.status', fallbackLabel: 'Status' },
        { key: 'sslStatus', labelKey: 'storefront.domains.sslLabel', fallbackLabel: 'SSL' },
        { key: 'createdAtUtc', sortKey: 'created', labelKey: 'storefront.domains.created', fallbackLabel: 'Created' },
    ]

    return (
        <div className="space-y-4">
            <AuthoringButton
                action={{
                    label: t('common.back', 'Back'),
                    onClick: () => handleNavigate('/storefront/domains'),
                }}
                defaultLabel={t('common.back', 'Back')}
            />

            <div className="flex items-center justify-between rounded-xl border border-gray-200 bg-white px-4 py-3 dark:border-gray-700 dark:bg-gray-800">
                <div>
                    <h1 className="text-xl font-semibold text-gray-900 dark:text-gray-100">
                        {t('storefront.domains.domainList', 'Domain List')}
                    </h1>
                    <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
                        {t('storefront.domains.domainListDesc', 'All domains for your tenant.')}
                    </p>
                </div>
                <button
                    type="button"
                    onClick={() => handleNavigate('/storefront/domains/add')}
                    className="flex items-center gap-2 rounded-lg border border-indigo-500 bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 disabled:opacity-50"
                    aria-label={t('storefront.domains.addDomain', 'Add Domain')}
                >
                    {t('storefront.domains.addDomain', 'Add Domain')}
                </button>
            </div>

            {/* Search & Filter bar */}
            <div className="flex flex-wrap items-center gap-3 rounded-xl border border-gray-200 bg-white px-4 py-3 dark:border-gray-700 dark:bg-gray-800">
                <div className="relative flex-1 min-w-48">
                    <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 dark:text-gray-500" />
                    <input
                        type="text"
                        placeholder={t('storefront.domains.searchPlaceholder', 'Search by hostname')}
                        value={searchTerm}
                        onChange={handleSearch}
                        className="w-full rounded-lg border border-gray-300 pl-10 pr-3 py-2 text-sm text-gray-900 outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100 dark:focus:border-indigo-400"
                    />
                </div>
                <button
                    type="button"
                    disabled
                    className="flex items-center gap-2 rounded-lg border border-gray-300 px-3 py-2 text-sm font-medium text-gray-500 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-600 dark:text-gray-400"
                >
                    <Filter size={16} />
                    {t('storefront.domains.filter', 'Filter')}
                </button>
                <button
                    type="button"
                    disabled
                    className="flex items-center gap-2 rounded-lg border border-gray-300 px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-600 dark:text-gray-300 dark:hover:bg-gray-700/50"
                >
                    <span className="text-xs">All types</span>
                </button>
            </div>

            {/* Error state */}
            {isError && (
                <AuthoringFeedback
                    kind="error"
                    title={t('storefront.domains.loadError', 'Unable to load domains')}
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

            {/* Loading */}
            {isLoading && (
                <div className="rounded-xl border border-gray-200 bg-white p-8 dark:border-gray-700 dark:bg-gray-800">
                    <AuthoringLoadingState />
                </div>
            )}

            {/* Empty */}
            {isEmpty && (
                <AuthoringEmptyState
                    title={t('storefront.domains.noDomains', 'No domains found')}
                    description={t(
                        'storefront.domains.noDomainsDesc',
                        'No domains match your search.',
                    )}
                />
            )}

            {/* Table */}
            {isSuccess && domains.length > 0 && (
                <div className="overflow-x-auto rounded-xl border border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-800">
                    <table
                        className="w-full"
                        role="table"
                        aria-label={t('storefront.domains.domainList', 'Domain List')}
                    >
                        <thead>
                            <tr className="border-b border-gray-200 bg-gray-50 dark:border-gray-700 dark:bg-gray-800/50">
                                {columns.map((col) => {
                                    const isActive = params.sortBy === col.sortKey
                                    const direction = isActive ? (params.sortDir === 'asc' ? 'asc' : 'desc') : null
                                    return (
                                        <th
                                            key={col.key}
                                            className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-gray-400"
                                            aria-sort={col.sortKey ? direction === 'asc' ? 'ascending' : direction === 'desc' ? 'descending' : 'none' : undefined}
                                        >
                                            {col.sortKey ? <button
                                                type="button"
                                                onClick={() => handleSort(col.sortKey)}
                                                aria-label={t(
                                                    'storefront.domains.sortBy',
                                                    'Sort by {{field}}',
                                                    { field: t(col.labelKey, col.fallbackLabel) },
                                                )}
                                                className="flex items-center gap-1 text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200"
                                            >
                                                {t(col.labelKey, col.fallbackLabel)}
                                                <ArrowUpDown size={14} aria-hidden="true" />
                                            </button> : t(col.labelKey, col.fallbackLabel)}
                                        </th>
                                    )
                                })}
                                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase dark:text-gray-400">
                                    {t('storefront.domains.actions', 'Actions')}
                                </th>
                            </tr>
                        </thead>
                        <tbody>
                            {domains.map((domain) => (
                                <tr
                                    key={domain.id}
                                    className="border-b border-gray-200 last:border-0 dark:border-gray-700"
                                >
                                    {columns.map((col) => (
                                        <td
                                            key={col.key}
                                            className="px-4 py-3 text-sm text-gray-900 dark:text-gray-100"
                                        >
                                            {col.key === 'hostName' ? <button type="button" onClick={() => handleNavigate(`/storefront/domains/${domain.id}`)} className="text-indigo-700 underline">{domain.hostName}</button> : domain[col.key]}
                                        </td>
                                    ))}
                                    <td className="px-4 py-3 text-right">
                                        <div className="relative inline-block">
                                            <button
                                                type="button"
                                                onClick={() => openActionMenu(domain.id)}
                                                className="rounded-lg border border-gray-300 p-1 text-gray-600 hover:bg-gray-100 dark:border-gray-600 dark:text-gray-400"
                                                aria-label={t(
                                                    'storefront.domains.rowActions',
                                                    'Row actions for {{hostname}}',
                                                    { hostname: domain.hostName },
                                                )}
                                            >
                                                <MoreVertical size={16} />
                                            </button>
                                            {dropdownOpenId === domain.id && (
                                                <div
                                                    className="absolute right-0 z-10 mt-1 w-40 origin-top-right rounded-md border border-gray-200 bg-white shadow-lg dark:border-gray-700 dark:bg-gray-800"
                                                    onMouseLeave={() => setDropdownOpenId(null)}
                                                >
                                                    {getAvailableActions(domain).map((actionName) => {
                                                        const iconMap = {
                                                            startVerification: Shield,
                                                            promote: Shield,
                                                            archive: Archive,
                                                            restore: RotateCcw,
                                                            delete: Trash2,
                                                        }
                                                        const Icon = iconMap[actionName]
                                                        const isDestructive = actionName === 'delete' || actionName === 'archive'
                                                        const labels = {
                                                            startVerification: t('storefront.domains.startVerification', 'Start Verification'),
                                                            promote: t('storefront.domains.promotePrimary', 'Promote to Primary'),
                                                            archive: t('storefront.domains.archive', 'Archive'),
                                                            restore: t('storefront.domains.restore', 'Restore'),
                                                            delete: t('storefront.domains.delete', 'Delete'),
                                                        }
                                                        return (
                                                            <button
                                                                key={actionName}
                                                                type="button"
                                                                onClick={() => handleAction(actionName, domain)}
                                                                disabled={isMutating}
                                                                className={`flex w-full items-center gap-2 px-3 py-2 text-sm ${
                                                                    isDestructive
                                                                        ? 'text-red-700 hover:bg-red-50 dark:text-red-300 dark:hover:bg-red-900/30'
                                                                        : 'text-gray-700 hover:bg-gray-50 dark:text-gray-300 dark:hover:bg-gray-700/50'
                                                                }`}
                                                            >
                                                                <Icon size={14} />
                                                                {labels[actionName]}
                                                            </button>
                                                        )
                                                    })}
                                                </div>
                                            )}
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            {/* Pagination */}
            {data && (
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

            {/* Confirmation Dialog — DOMAIN-3.1-C */}
            {confirmDialog.open && (
                <AuthoringConfirmDialog
                    open={confirmDialog.open}
                    title={getActionDialogConfig()?.title}
                    description={getActionDialogConfig()?.description}
                    confirmLabel={getActionDialogConfig()?.confirmLabel}
                    confirmTone={getActionDialogConfig()?.destructive ? 'danger' : 'primary'}
                    busy={isMutating}
                    onConfirm={handleConfirmAction}
                    onClose={() => setConfirmDialog({ open: false, action: null, domain: null })}
                />
            )}

            {/* Mutation error feedback */}
            {mutationError && (
                <AuthoringFeedback
                    kind="error"
                    title={t('storefront.domains.actionError', 'Action failed')}
                    message={mutationError}
                />
            )}
        </div>
    )
}

export default DomainListPage

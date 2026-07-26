import { useEffect, useMemo, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, EyeOff, RefreshCw, RotateCcw, Search, ShieldCheck } from 'lucide-react'
import { useDebounce } from '../../hooks/useDebounce'
import {
    getMarketplaceGovernanceDashboard,
    getMarketplaceGovernanceListings,
    restoreMarketplaceListing,
    restoreMarketplaceTenant,
    suppressMarketplaceListing,
    suspendMarketplaceTenant,
    type GovernanceListingStatus,
    type MarketplaceGovernanceListing,
    type MarketplaceGovernanceListingFilters,
} from './marketplaceGovernanceService'

const DEFAULT_FILTERS: MarketplaceGovernanceListingFilters = {
    page: 1,
    pageSize: 50,
    q: '',
    status: '',
    vendorStatus: '',
    tenantStatus: '',
    overrideEnabled: '',
}

function badgeClasses(value: string): string {
    if (value === 'suppressed' || value === 'Suspended') return 'border-amber-200 bg-amber-50 text-amber-900'
    if (value === 'orphaned' || value === 'Banned' || value === 'Deleted') return 'border-rose-200 bg-rose-50 text-rose-900'
    if (value === 'published' || value === 'Active') return 'border-emerald-200 bg-emerald-50 text-emerald-900'
    return 'border-slate-200 bg-slate-100 text-slate-800'
}

function formatDate(value: string | null | undefined): string {
    if (!value) return 'Not recorded'
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? 'Not recorded' : date.toLocaleString()
}

function getErrorMessage(error: unknown, fallback: string): string {
    return error instanceof Error ? error.message : fallback
}

export default function MarketplaceGovernancePage() {
    const queryClient = useQueryClient()
    const [filters, setFilters] = useState(DEFAULT_FILTERS)
    const [selectedListingId, setSelectedListingId] = useState<string | null>(null)
    const [isActionRunning, setIsActionRunning] = useState(false)
    const [actionError, setActionError] = useState<string | null>(null)
    const [actionMessage, setActionMessage] = useState<string | null>(null)
    const debouncedSearch = useDebounce(filters.q ?? '', 350)

    const effectiveFilters = useMemo(
        () => ({
            ...filters,
            q: debouncedSearch,
        }),
        [debouncedSearch, filters],
    )

    const dashboardQuery = useQuery({
        queryKey: ['super-admin', 'marketplace-governance', 'dashboard'],
        queryFn: getMarketplaceGovernanceDashboard,
    })

    const listingsQuery = useQuery({
        queryKey: ['super-admin', 'marketplace-governance', 'listings', effectiveFilters],
        queryFn: () => getMarketplaceGovernanceListings(effectiveFilters),
    })

    const listings = listingsQuery.data?.listings ?? []
    const selectedListing = listings.find((listing) => listing.listingId === selectedListingId) ?? null
    const totalPages = listingsQuery.data?.meta?.totalPages ?? 0
    const totalCount = listingsQuery.data?.meta?.totalCount ?? 0

    useEffect(() => {
        if (selectedListingId && !selectedListing && !listingsQuery.isFetching) {
            setSelectedListingId(null)
        }
    }, [listingsQuery.isFetching, selectedListing, selectedListingId])

    function updateFilter<K extends keyof MarketplaceGovernanceListingFilters>(
        key: K,
        value: MarketplaceGovernanceListingFilters[K],
    ) {
        setFilters((current) => ({
            ...current,
            [key]: value,
            page: key === 'page' || key === 'pageSize' ? (value as number) : 1,
        }))
    }

    async function refreshAll() {
        await Promise.all([
            dashboardQuery.refetch(),
            listingsQuery.refetch(),
        ])
    }

    async function runListingAction(action: 'suppress' | 'restore') {
        if (!selectedListing) return

        const reason = window.prompt(`${action === 'suppress' ? 'Suppress' : 'Restore'} listing reason`)?.trim()
        if (!reason || reason.length < 3) return

        setActionError(null)
        setActionMessage(null)
        setIsActionRunning(true)
        try {
            const result = action === 'suppress'
                ? await suppressMarketplaceListing(selectedListing.listingId, reason)
                : await restoreMarketplaceListing(selectedListing.listingId, reason)

            setActionMessage(result.message)
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'marketplace-governance'] })
        } catch (error) {
            setActionError(getErrorMessage(error, 'Marketplace governance action failed.'))
        } finally {
            setIsActionRunning(false)
        }
    }

    async function runTenantAction(action: 'suspend' | 'restore') {
        if (!selectedListing) return

        const reason = window.prompt(`${action === 'suspend' ? 'Suspend' : 'Restore'} tenant reason`)?.trim()
        if (!reason || reason.length < 3) return

        setActionError(null)
        setActionMessage(null)
        setIsActionRunning(true)
        try {
            const result = action === 'suspend'
                ? await suspendMarketplaceTenant(selectedListing.tenantId, reason)
                : await restoreMarketplaceTenant(selectedListing.tenantId, reason)

            setActionMessage(result.message)
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'marketplace-governance'] })
        } catch (error) {
            setActionError(getErrorMessage(error, 'Tenant governance action failed.'))
        } finally {
            setIsActionRunning(false)
        }
    }

    const dashboard = dashboardQuery.isFetching ? undefined : dashboardQuery.data
    const actionDisabled = listingsQuery.isFetching || isActionRunning

    return (
        <div className="space-y-6">
            <section className="rounded-[30px] border border-slate-200 bg-white p-6 shadow-sm">
                <div className="flex flex-col gap-5 xl:flex-row xl:items-center xl:justify-between">
                    <div>
                        <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800">
                            <ShieldCheck className="h-4 w-4" />
                            SQL authority console
                        </div>
                        <h1 className="mt-4 text-3xl font-semibold tracking-tight text-slate-950">Marketplace Governance</h1>
                        <p className="mt-3 max-w-3xl text-sm leading-6 text-slate-600">
                            Super Admin visibility controls for listing suppression, tenant governance, vendor governance visibility state, filters, and audit-ready operations.
                        </p>
                    </div>
                    <button
                        type="button"
                        className="inline-flex items-center justify-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white"
                        onClick={() => refreshAll()}
                    >
                        <RefreshCw className={`h-4 w-4 ${dashboardQuery.isFetching || listingsQuery.isFetching ? 'animate-spin' : ''}`} />
                        Refresh
                    </button>
                </div>

                <div className="mt-6 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
                    <Metric label="Listings" value={dashboard?.totalListings ?? 0} />
                    <Metric label="Published" value={dashboard?.publishedListings ?? 0} tone="emerald" />
                    <Metric label="Suppressed" value={dashboard?.suppressedListings ?? 0} tone="amber" />
                    <Metric label="Orphaned" value={dashboard?.orphanedListings ?? 0} tone="rose" />
                    <Metric label="Audit events" value={dashboard?.recentGovernanceAuditCount ?? 0} />
                </div>
                <div className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
                    <Metric label="Active vendors" value={dashboard?.activeVendors ?? 0} />
                    <Metric label="Suspended vendors" value={dashboard?.suspendedVendors ?? 0} tone="amber" />
                    <Metric label="Banned vendors" value={dashboard?.bannedVendors ?? 0} tone="rose" />
                    <Metric label="Active tenants" value={dashboard?.activeTenants ?? 0} />
                    <Metric label="Suspended tenants" value={dashboard?.suspendedTenants ?? 0} tone="amber" />
                </div>
            </section>

            {actionError ? (
                <section className="rounded-2xl border border-rose-200 bg-rose-50 px-5 py-4 text-sm text-rose-900">
                    {actionError}
                </section>
            ) : null}

            {actionMessage ? (
                <section className="rounded-2xl border border-emerald-200 bg-emerald-50 px-5 py-4 text-sm text-emerald-900">
                    {actionMessage}
                </section>
            ) : null}

            <section className="rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm">
                <div className="grid gap-3 xl:grid-cols-6">
                    <div className="relative xl:col-span-2">
                        <Search className="pointer-events-none absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                        <input
                            value={filters.q}
                            onChange={(event) => updateFilter('q', event.target.value)}
                            placeholder="Search listing, tenant, SKU, category"
                            className="w-full rounded-2xl border border-slate-200 py-3 pl-11 pr-4 text-sm text-slate-900"
                        />
                    </div>
                    <select value={filters.status} onChange={(event) => updateFilter('status', event.target.value as GovernanceListingStatus)} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm">
                        <option value="">All visibility</option>
                        <option value="published">Published</option>
                        <option value="unpublished">Unpublished</option>
                        <option value="suppressed">Suppressed</option>
                        <option value="orphaned">Orphaned</option>
                        <option value="vendor_suspended">Vendor suspended</option>
                        <option value="tenant_suspended">Tenant suspended</option>
                    </select>
                    <select value={filters.vendorStatus} onChange={(event) => updateFilter('vendorStatus', event.target.value as MarketplaceGovernanceListingFilters['vendorStatus'])} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm">
                        <option value="">All vendors</option>
                        <option value="Active">Vendor active</option>
                        <option value="Suspended">Vendor suspended</option>
                        <option value="Banned">Vendor banned</option>
                        <option value="FinanciallyLocked">Vendor financial lock</option>
                    </select>
                    <select value={filters.tenantStatus} onChange={(event) => updateFilter('tenantStatus', event.target.value as MarketplaceGovernanceListingFilters['tenantStatus'])} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm">
                        <option value="">All tenants</option>
                        <option value="Active">Tenant active</option>
                        <option value="Suspended">Tenant suspended</option>
                        <option value="Deleted">Tenant deleted</option>
                    </select>
                    <select value={filters.overrideEnabled} onChange={(event) => updateFilter('overrideEnabled', event.target.value as MarketplaceGovernanceListingFilters['overrideEnabled'])} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm">
                        <option value="">All prices</option>
                        <option value="true">Override present</option>
                        <option value="false">No override</option>
                    </select>
                </div>
            </section>

            <div className="grid gap-6 xl:grid-cols-[minmax(0,1.35fr)_minmax(360px,0.8fr)]">
                <section className="overflow-hidden rounded-[30px] border border-slate-200 bg-white shadow-sm">
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-slate-200">
                            <thead className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                <tr>
                                    <th className="px-5 py-4">Listing</th>
                                    <th className="px-5 py-4">Tenant</th>
                                    <th className="px-5 py-4">Visibility</th>
                                    <th className="px-5 py-4">Vendor</th>
                                    <th className="px-5 py-4">Category</th>
                                    <th className="px-5 py-4">Updated</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-slate-100 text-sm text-slate-700">
                                {listings.map((listing) => (
                                    <tr
                                        key={listing.listingId}
                                        className={`cursor-pointer hover:bg-slate-50 ${selectedListingId === listing.listingId ? 'bg-emerald-50/70' : ''}`}
                                        onClick={() => setSelectedListingId(listing.listingId)}
                                    >
                                        <td className="px-5 py-4">
                                            <p className="font-semibold text-slate-950">{listing.productName}</p>
                                            <p className="mt-1 text-xs text-slate-500">{listing.sku ?? listing.variantName ?? listing.variantId}</p>
                                        </td>
                                        <td className="px-5 py-4">
                                            <p className="font-medium text-slate-900">{listing.tenantName}</p>
                                            <span className={`mt-2 inline-flex rounded-full border px-3 py-1 text-xs font-semibold ${badgeClasses(listing.tenantStatus)}`}>{listing.tenantStatus}</span>
                                        </td>
                                        <td className="px-5 py-4">
                                            <span className={`rounded-full border px-3 py-1 text-xs font-semibold ${badgeClasses(listing.visibilityState)}`}>{listing.visibilityState}</span>
                                            {listing.isOrphaned ? <p className="mt-2 text-xs text-rose-700">{listing.orphanReason}</p> : null}
                                        </td>
                                        <td className="px-5 py-4">
                                            <span className={`rounded-full border px-3 py-1 text-xs font-semibold ${badgeClasses(listing.vendorGovernanceStatus)}`}>{listing.vendorGovernanceStatus}</span>
                                        </td>
                                        <td className="px-5 py-4">{listing.categoryName ?? listing.categoryId}</td>
                                        <td className="px-5 py-4">{formatDate(listing.updatedAtUtc ?? listing.createdAtUtc)}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                    <div className="flex items-center justify-between border-t border-slate-100 px-5 py-4 text-sm text-slate-600">
                        <p>{totalCount} listings, page {filters.page} of {totalPages || 1}</p>
                        <div className="flex items-center gap-3">
                            <button type="button" disabled={filters.page <= 1} className="rounded-2xl border border-slate-200 px-4 py-3 disabled:opacity-50" onClick={() => updateFilter('page', filters.page - 1)}>Previous</button>
                            <button type="button" disabled={!totalPages || filters.page >= totalPages} className="rounded-2xl bg-slate-950 px-4 py-3 font-semibold text-white disabled:opacity-50" onClick={() => updateFilter('page', filters.page + 1)}>Next</button>
                        </div>
                    </div>
                </section>

                <section className="rounded-[30px] border border-slate-200 bg-white p-5 shadow-sm">
                    {!selectedListing ? (
                        <div className="grid min-h-[420px] place-items-center text-center">
                            <div>
                                <AlertTriangle className="mx-auto h-8 w-8 text-slate-400" />
                                <p className="mt-4 text-sm font-semibold uppercase tracking-[0.22em] text-slate-500">Listing details</p>
                                <p className="mt-3 text-sm leading-6 text-slate-600">Select a listing to inspect SQL authority, suppression state, and governance actions.</p>
                            </div>
                        </div>
                    ) : (
                        <div className="space-y-5">
                            <div className="rounded-[26px] border border-slate-200 bg-slate-50 p-5">
                                <span className={`rounded-full border px-3 py-1 text-xs font-semibold ${badgeClasses(selectedListing.visibilityState)}`}>{selectedListing.visibilityState}</span>
                                <h2 className="mt-4 text-2xl font-semibold text-slate-950">{selectedListing.productName}</h2>
                                <p className="mt-2 text-sm text-slate-600">{selectedListing.tenantName} · {selectedListing.sku ?? selectedListing.variantId}</p>
                            </div>

                            {selectedListing.isSuppressed ? (
                                <div className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
                                    {selectedListing.suppressionReason ?? 'Listing is hidden by marketplace governance.'}
                                </div>
                            ) : null}

                            {selectedListing.isOrphaned ? (
                                <div className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-900">
                                    {selectedListing.orphanReason}
                                </div>
                            ) : null}

                            <div className="grid gap-3">
                                <DetailRow label="Listing" value={selectedListing.listingId} />
                                <DetailRow label="Tenant" value={`${selectedListing.tenantName} (${selectedListing.tenantStatus})`} />
                                <DetailRow label="Vendor governance" value={selectedListing.vendorGovernanceStatus} />
                                <DetailRow label="Suppression owner" value={selectedListing.suppressionOwner ?? 'None'} />
                                <DetailRow label="Suppressed at" value={formatDate(selectedListing.suppressionTimestampUtc)} />
                                <DetailRow label="Override present" value={selectedListing.overridePresent ? `Yes, v${selectedListing.priceOverrideMutationVersion}` : 'No'} />
                            </div>

                            <div className="flex flex-wrap gap-3">
                                <button type="button" disabled={actionDisabled} className="inline-flex items-center gap-2 rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm font-semibold text-amber-900 disabled:opacity-50" onClick={() => runListingAction('suppress')}>
                                    <EyeOff className="h-4 w-4" />
                                    Suppress listing
                                </button>
                                <button type="button" disabled={actionDisabled} className="inline-flex items-center gap-2 rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm font-semibold text-emerald-900 disabled:opacity-50" onClick={() => runListingAction('restore')}>
                                    <RotateCcw className="h-4 w-4" />
                                    Restore listing
                                </button>
                                <button type="button" disabled={actionDisabled} className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-semibold text-rose-900 disabled:opacity-50" onClick={() => runTenantAction('suspend')}>
                                    Suspend tenant
                                </button>
                                <button type="button" disabled={actionDisabled} className="rounded-2xl border border-cyan-200 bg-cyan-50 px-4 py-3 text-sm font-semibold text-cyan-900 disabled:opacity-50" onClick={() => runTenantAction('restore')}>
                                    Restore tenant
                                </button>
                            </div>
                        </div>
                    )}
                </section>
            </div>
        </div>
    )
}

function Metric({ label, value, tone = 'slate' }: { label: string; value: number; tone?: 'slate' | 'emerald' | 'amber' | 'rose' }) {
    const toneClass = {
        slate: 'border-slate-200 bg-white text-slate-950',
        emerald: 'border-emerald-200 bg-emerald-50 text-emerald-950',
        amber: 'border-amber-200 bg-amber-50 text-amber-950',
        rose: 'border-rose-200 bg-rose-50 text-rose-950',
    }[tone]

    return (
        <div className={`rounded-2xl border px-4 py-3 ${toneClass}`}>
            <p className="text-xs font-semibold uppercase tracking-[0.18em] opacity-70">{label}</p>
            <p className="mt-2 text-2xl font-semibold">{value}</p>
        </div>
    )
}

function DetailRow({ label, value }: { label: string; value: string }) {
    return (
        <div className="rounded-2xl border border-slate-200 bg-white px-4 py-3">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">{label}</p>
            <p className="mt-1 break-words text-sm font-medium text-slate-900">{value}</p>
        </div>
    )
}

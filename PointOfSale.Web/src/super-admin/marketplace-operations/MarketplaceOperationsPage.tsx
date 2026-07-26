import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Activity, Database, RefreshCw, RotateCw, SearchCheck, ShieldCheck, Wrench } from 'lucide-react'
import {
    getMarketplaceOperationsDashboard,
    runGlobalMarketplaceReindex,
    runMarketplaceCatchUpReindex,
    runTenantMarketplaceReindex,
    verifyMarketplaceListing,
    verifyMarketplaceTenant,
} from './marketplaceOperationsService'

function formatDate(value: string | null | undefined): string {
    if (!value) return 'Not recorded'
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? 'Not recorded' : date.toLocaleString()
}

function errorMessage(error: unknown, fallback: string): string {
    return error instanceof Error ? error.message : fallback
}

export default function MarketplaceOperationsPage() {
    const queryClient = useQueryClient()
    const [tenantId, setTenantId] = useState('')
    const [listingId, setListingId] = useState('')
    const [message, setMessage] = useState<string | null>(null)
    const [error, setError] = useState<string | null>(null)
    const [busyAction, setBusyAction] = useState<string | null>(null)

    const dashboardQuery = useQuery({
        queryKey: ['super-admin', 'marketplace-operations', 'dashboard'],
        queryFn: getMarketplaceOperationsDashboard,
        refetchInterval: 30000,
    })

    const dashboard = dashboardQuery.data

    async function runAction(action: string, work: (reason: string) => Promise<unknown>) {
        const reason = window.prompt('Operation reason')?.trim()
        if (!reason || reason.length < 3) return

        setBusyAction(action)
        setError(null)
        setMessage(null)
        try {
            const result = await work(reason)
            setMessage(`${action} queued or completed. ${JSON.stringify(result)}`)
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'marketplace-operations'] })
        } catch (caught) {
            setError(errorMessage(caught, `${action} failed.`))
        } finally {
            setBusyAction(null)
        }
    }

    return (
        <div className="space-y-6">
            <section className="rounded-[30px] border border-slate-200 bg-white p-6 shadow-sm">
                <div className="flex flex-col gap-5 xl:flex-row xl:items-center xl:justify-between">
                    <div>
                        <div className="inline-flex items-center gap-2 rounded-full border border-cyan-200 bg-cyan-50 px-4 py-2 text-sm font-semibold text-cyan-900">
                            <ShieldCheck className="h-4 w-4" />
                            SQL-derived recovery operations
                        </div>
                        <h1 className="mt-4 text-3xl font-semibold tracking-tight text-slate-950">Marketplace Operations</h1>
                        <p className="mt-3 max-w-3xl text-sm leading-6 text-slate-600">
                            Reindex, search sync, projection recovery, auto-healing queue state, diagnostics, and repair controls.
                        </p>
                    </div>
                    <button
                        type="button"
                        className="inline-flex items-center justify-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white"
                        onClick={() => dashboardQuery.refetch()}
                    >
                        <RefreshCw className={`h-4 w-4 ${dashboardQuery.isFetching ? 'animate-spin' : ''}`} />
                        Refresh
                    </button>
                </div>

                <div className="mt-6 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
                    <Metric icon={Database} label="SQL listings" value={dashboard?.sqlListings ?? 0} />
                    <Metric icon={SearchCheck} label="SQL published" value={dashboard?.sqlPublishedListings ?? 0} tone="emerald" />
                    <Metric icon={Wrench} label="Suppressed" value={dashboard?.sqlSuppressedListings ?? 0} tone="amber" />
                    <Metric icon={Activity} label="Mongo docs" value={dashboard?.mongoProjectedListings ?? 0} />
                    <Metric icon={SearchCheck} label="ES active docs" value={dashboard?.elasticsearchActiveDocumentCount ?? 0} />
                </div>

                <div className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
                    <Metric icon={RotateCw} label="Search pending" value={dashboard?.searchSyncPendingListings ?? 0} tone="amber" />
                    <Metric icon={RotateCw} label="Search failed" value={dashboard?.searchSyncFailedListings ?? 0} tone="rose" />
                    <Metric icon={Activity} label="Healing queue" value={dashboard?.autoHealing.queueDepth ?? 0} />
                    <Metric icon={Activity} label="Healing failures" value={dashboard?.autoHealing.failureCount ?? 0} tone="rose" />
                    <Metric icon={Activity} label="Dropped" value={dashboard?.autoHealing.droppedCount ?? 0} tone="amber" />
                </div>
            </section>

            {error ? <section className="rounded-2xl border border-rose-200 bg-rose-50 px-5 py-4 text-sm text-rose-900">{error}</section> : null}
            {message ? <section className="rounded-2xl border border-emerald-200 bg-emerald-50 px-5 py-4 text-sm text-emerald-900">{message}</section> : null}

            <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_420px]">
                <section className="rounded-[30px] border border-slate-200 bg-white p-5 shadow-sm">
                    <h2 className="text-lg font-semibold text-slate-950">Search Diagnostics</h2>
                    <div className="mt-4 grid gap-3 md:grid-cols-2">
                        <Detail label="Active index" value={dashboard?.searchIndexStatus.activeIndexName ?? 'Not available'} />
                        <Detail label="Write index" value={dashboard?.searchIndexStatus.writeIndexName ?? 'Not available'} />
                        <Detail label="Desired index" value={dashboard?.searchIndexStatus.desiredIndexName ?? 'Not available'} />
                        <Detail label="Requires reindex" value={dashboard?.searchIndexStatus.requiresReindex ? 'Yes' : 'No'} />
                        <Detail label="Mapping compatible" value={dashboard?.searchIndexStatus.mappingCompatible ? 'Yes' : 'No'} />
                        <Detail label="Smoke test" value={dashboard?.searchIndexVerification.smokeTestPassed ? 'Passed' : 'Failed'} />
                        <Detail label="Circuit breaker" value={dashboard?.autoHealing.circuitBreakerState ?? 'Unknown'} />
                        <Detail label="Last background scan" value={formatDate(dashboard?.autoHealing.lastBackgroundReconciledAtUtc)} />
                    </div>
                </section>

                <section className="rounded-[30px] border border-slate-200 bg-white p-5 shadow-sm">
                    <h2 className="text-lg font-semibold text-slate-950">Repair Controls</h2>
                    <div className="mt-4 space-y-3">
                        <button type="button" className="w-full rounded-2xl bg-slate-950 px-4 py-3 text-sm font-semibold text-white disabled:opacity-50" disabled={busyAction != null} onClick={() => runAction('Global reindex', runGlobalMarketplaceReindex)}>
                            Global reindex
                        </button>
                        <button type="button" className="w-full rounded-2xl border border-cyan-200 bg-cyan-50 px-4 py-3 text-sm font-semibold text-cyan-900 disabled:opacity-50" disabled={busyAction != null} onClick={() => runAction('Catch-up reindex', runMarketplaceCatchUpReindex)}>
                            Catch-up reindex
                        </button>
                    </div>

                    <div className="mt-6 space-y-3">
                        <input value={tenantId} onChange={(event) => setTenantId(event.target.value)} placeholder="Tenant ID" className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm" />
                        <div className="grid grid-cols-2 gap-3">
                            <button type="button" className="rounded-2xl border border-slate-200 px-4 py-3 text-sm font-semibold disabled:opacity-50" disabled={!tenantId.trim() || busyAction != null} onClick={() => runAction('Tenant reindex', (reason) => runTenantMarketplaceReindex(tenantId.trim(), reason))}>
                                Tenant reindex
                            </button>
                            <button type="button" className="rounded-2xl border border-slate-200 px-4 py-3 text-sm font-semibold disabled:opacity-50" disabled={!tenantId.trim() || busyAction != null} onClick={() => runAction('Tenant verify', (reason) => verifyMarketplaceTenant(tenantId.trim(), reason))}>
                                Verify tenant
                            </button>
                        </div>
                    </div>

                    <div className="mt-6 space-y-3">
                        <input value={listingId} onChange={(event) => setListingId(event.target.value)} placeholder="Listing ID" className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm" />
                        <button type="button" className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm font-semibold disabled:opacity-50" disabled={!listingId.trim() || busyAction != null} onClick={() => runAction('Listing verify', (reason) => verifyMarketplaceListing(listingId.trim(), reason))}>
                            Verify listing
                        </button>
                    </div>
                </section>
            </div>
        </div>
    )
}

function Metric({ icon: Icon, label, value, tone = 'slate' }: { icon: typeof Activity; label: string; value: number; tone?: 'slate' | 'emerald' | 'amber' | 'rose' }) {
    const toneClass = {
        slate: 'border-slate-200 bg-white text-slate-950',
        emerald: 'border-emerald-200 bg-emerald-50 text-emerald-950',
        amber: 'border-amber-200 bg-amber-50 text-amber-950',
        rose: 'border-rose-200 bg-rose-50 text-rose-950',
    }[tone]

    return (
        <div className={`rounded-2xl border px-4 py-3 ${toneClass}`}>
            <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.18em] opacity-70">
                <Icon className="h-4 w-4" />
                {label}
            </div>
            <p className="mt-2 text-2xl font-semibold">{value}</p>
        </div>
    )
}

function Detail({ label, value }: { label: string; value: string }) {
    return (
        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">{label}</p>
            <p className="mt-1 break-words text-sm font-medium text-slate-900">{value}</p>
        </div>
    )
}

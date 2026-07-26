import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
    BadgePercent,
    Layers3,
    RefreshCw,
    ShieldAlert,
    ShieldCheck,
    Sparkles,
} from 'lucide-react'
import { useToast } from '../../components/Toast'
import CommissionFormModal from './CommissionFormModal'
import CommissionPreviewPanel from './CommissionPreviewPanel'
import CommissionTable from './CommissionTable'
import {
    createCommissionRule,
    deactivateCommissionRule,
    getCommissionCategories,
    getCommissionRules,
    getCommissionVendors,
    resolveCommissionPreview,
    updateCommissionRule,
    type CommissionRule,
    type CommissionRuleFormInput,
    type CommissionScopeType,
} from './commissionService'

const commissionRulesQueryKey = ['super-admin', 'commission', 'rules'] as const
const commissionCategoriesQueryKey = ['super-admin', 'commission', 'categories'] as const
const commissionVendorsQueryKey = ['super-admin', 'commission', 'vendors'] as const

function formatPercent(rate: number): string {
    return `${new Intl.NumberFormat('en-US', {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2,
    }).format(rate * 100)}%`
}

function formatTimestamp(value?: string | null): string {
    if (!value) {
        return 'Unavailable'
    }

    const parsed = new Date(value)
    if (Number.isNaN(parsed.getTime())) {
        return 'Unavailable'
    }

    return parsed.toLocaleString()
}

function getErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof Error && error.message) {
        return error.message
    }

    return fallback
}

function truncateId(value?: string | null): string {
    if (!value) {
        return 'Unavailable'
    }

    if (value.length <= 14) {
        return value
    }

    return `${value.slice(0, 8)}...${value.slice(-4)}`
}

function formatAppliedRuleLabel(scopeType: CommissionScopeType | 'Legacy', scopeName?: string | null): string {
    if (scopeType === 'Legacy') {
        return 'Applied Rule: Legacy fallback'
    }

    if (scopeType === 'Global') {
        return 'Applied Rule: Global'
    }

    if (!scopeName) {
        return `Applied Rule: ${scopeType}`
    }

    return `Applied Rule: ${scopeType} (${scopeName})`
}

function PageSkeleton() {
    return (
        <div className="space-y-6">
            <section className="overflow-hidden rounded-[36px] border border-slate-200 bg-white p-8 shadow-[0_36px_100px_-56px_rgba(15,23,42,0.45)]">
                <div className="space-y-4 animate-pulse">
                    <div className="h-7 w-40 rounded-full bg-slate-200" />
                    <div className="h-12 w-2/3 rounded-2xl bg-slate-200" />
                    <div className="h-6 w-1/2 rounded-full bg-slate-100" />
                    <div className="grid gap-4 pt-4 sm:grid-cols-3">
                        {Array.from({ length: 3 }).map((_, index) => (
                            <div key={`commission-page-skeleton-card-${index}`} className="h-28 rounded-[28px] bg-slate-100" />
                        ))}
                    </div>
                </div>
            </section>

            <div className="grid gap-6 xl:grid-cols-[0.9fr_1.1fr]">
                {Array.from({ length: 2 }).map((_, index) => (
                    <section
                        key={`commission-page-skeleton-section-${index}`}
                        className="h-[360px] animate-pulse rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]"
                    />
                ))}
            </div>

            <section className="h-[420px] animate-pulse rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]" />
        </div>
    )
}

export default function CommissionManagementPage() {
    const queryClient = useQueryClient()
    const { showError, showSuccess } = useToast()
    const [isFormOpen, setIsFormOpen] = useState(false)
    const [editingRule, setEditingRule] = useState<CommissionRule | null>(null)
    const [defaultScopeType, setDefaultScopeType] = useState<CommissionScopeType>('Category')
    const [pendingDeactivation, setPendingDeactivation] = useState<CommissionRule | null>(null)

    const rulesQuery = useQuery({
        queryKey: commissionRulesQueryKey,
        queryFn: getCommissionRules,
        staleTime: 15_000,
        refetchOnWindowFocus: true,
    })

    const categoriesQuery = useQuery({
        queryKey: commissionCategoriesQueryKey,
        queryFn: getCommissionCategories,
        staleTime: 5 * 60_000,
    })

    const vendorsQuery = useQuery({
        queryKey: commissionVendorsQueryKey,
        queryFn: getCommissionVendors,
        staleTime: 5 * 60_000,
    })

    const upsertMutation = useMutation({
        mutationFn: async ({
            ruleId,
            input,
        }: {
            ruleId?: string
            input: CommissionRuleFormInput
        }) => {
            return ruleId
                ? updateCommissionRule(ruleId, input)
                : createCommissionRule(input)
        },
    })

    const deactivateMutation = useMutation({
        mutationFn: (ruleId: string) => deactivateCommissionRule(ruleId),
    })

    const previewMutation = useMutation({
        mutationFn: ({
            vendorId,
            categoryId,
        }: {
            vendorId?: string | null
            categoryId?: string | null
        }) => resolveCommissionPreview(vendorId, categoryId),
    })

    const rules = rulesQuery.data?.rules ?? []
    const categories = categoriesQuery.data ?? []
    const vendors = vendorsQuery.data ?? []
    const activeGlobalRule =
        rules.find((rule) => rule.scopeType === 'Global' && rule.isActive) ?? null
    const activeGlobalRuleCount = rules.filter((rule) => rule.scopeType === 'Global' && rule.isActive).length

    const categoryNamesById = useMemo(
        () =>
            new Map(
                categories.map((category) => [
                    category.id,
                    `${category.iconEmoji ? `${category.iconEmoji} ` : ''}${category.name}`,
                ]),
            ),
        [categories],
    )

    const vendorNamesById = useMemo(
        () => new Map(vendors.map((vendor) => [vendor.id, vendor.name])),
        [vendors],
    )

    const isInitialLoading = rulesQuery.isLoading && !rulesQuery.data
    const isRefreshing =
        rulesQuery.isFetching || categoriesQuery.isFetching || vendorsQuery.isFetching

    function resolveScopeName(rule: CommissionRule): string {
        if (rule.scopeType === 'Global') {
            return 'Platform default'
        }

        if (rule.scopeType === 'Category') {
            return categoryNamesById.get(rule.scopeId ?? '') ?? `Unknown category (${truncateId(rule.scopeId)})`
        }

        return vendorNamesById.get(rule.scopeId ?? '') ?? `Unknown vendor (${truncateId(rule.scopeId)})`
    }

    function getRuleLabel(rule: CommissionRule): string {
        return formatAppliedRuleLabel(
            rule.scopeType,
            rule.scopeType === 'Global' ? null : resolveScopeName(rule),
        )
    }

    function getDeactivationState(rule: CommissionRule): { disabled: boolean; reason?: string } {
        const isProtectedGlobalRule =
            rule.isActive &&
            rule.scopeType === 'Global' &&
            activeGlobalRuleCount === 1

        if (!isProtectedGlobalRule) {
            return { disabled: false }
        }

        return {
            disabled: true,
            reason: 'You must always keep one active global commission rule.',
        }
    }

    function openCreateModal(scopeType: CommissionScopeType) {
        setEditingRule(null)
        setDefaultScopeType(scopeType)
        setIsFormOpen(true)
    }

    function openEditModal(rule: CommissionRule) {
        setEditingRule(rule)
        setDefaultScopeType(rule.scopeType)
        setIsFormOpen(true)
    }

    function closeFormModal() {
        setIsFormOpen(false)
        setEditingRule(null)
    }

    async function refreshAll() {
        await Promise.all([
            rulesQuery.refetch(),
            categoriesQuery.refetch(),
            vendorsQuery.refetch(),
        ])
    }

    async function handleSubmitRule(input: CommissionRuleFormInput) {
        try {
            await upsertMutation.mutateAsync({
                ruleId: editingRule?.id,
                input,
            })

            await queryClient.invalidateQueries({ queryKey: commissionRulesQueryKey })
            previewMutation.reset()
            showSuccess(editingRule ? 'Commission rule updated successfully.' : 'Commission rule created successfully.')
            closeFormModal()
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to save commission rule.'))
        }
    }

    async function handleConfirmDeactivate() {
        if (!pendingDeactivation) {
            return
        }

        const deactivationState = getDeactivationState(pendingDeactivation)
        if (deactivationState.disabled) {
            showError(deactivationState.reason ?? 'This commission rule cannot be deactivated.')
            setPendingDeactivation(null)
            return
        }

        try {
            await deactivateMutation.mutateAsync(pendingDeactivation.id)
            await queryClient.invalidateQueries({ queryKey: commissionRulesQueryKey })
            previewMutation.reset()
            showSuccess('Commission rule deactivated successfully.')
            setPendingDeactivation(null)
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to deactivate commission rule.'))
        }
    }

    async function handlePreview(vendorId?: string | null, categoryId?: string | null) {
        try {
            await previewMutation.mutateAsync({ vendorId, categoryId })
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to preview commission resolution.'))
        }
    }

    if (rulesQuery.isError && !rulesQuery.data) {
        return (
            <div className="grid min-h-[60vh] place-items-center">
                <div className="w-full max-w-xl rounded-[32px] border border-slate-200 bg-white p-8 text-center shadow-[0_30px_80px_-48px_rgba(15,23,42,0.45)]">
                    <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                        <ShieldAlert className="h-6 w-6" />
                    </div>
                    <h2 className="mt-5 text-2xl font-semibold text-slate-950">
                        Commission control unavailable
                    </h2>
                    <p className="mt-3 text-sm leading-6 text-slate-500">
                        {getErrorMessage(rulesQuery.error, 'Unable to load commission rules.')}
                    </p>
                    <button
                        type="button"
                        className="mt-6 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                        onClick={() => refreshAll()}
                    >
                        <RefreshCw className="h-4 w-4" />
                        Retry request
                    </button>
                </div>
            </div>
        )
    }

    if (isInitialLoading) {
        return <PageSkeleton />
    }

    return (
        <>
            <div className="space-y-6">
                <section className="overflow-hidden rounded-[36px] border border-slate-200 bg-white shadow-[0_36px_100px_-56px_rgba(15,23,42,0.45)]">
                    <div className="relative overflow-hidden px-6 py-6 sm:px-8 sm:py-8">
                        <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(16,185,129,0.18),_transparent_28%),radial-gradient(circle_at_bottom_right,_rgba(14,165,233,0.14),_transparent_26%)]" />
                        <div className="relative flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
                            <div className="max-w-3xl">
                                <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800">
                                    <BadgePercent className="h-4 w-4" />
                                    Commission Management
                                </div>

                                <h1 className="mt-5 text-3xl font-semibold tracking-tight text-slate-950 sm:text-4xl">
                                    Control global defaults, precision overrides, and live resolution previews
                                </h1>
                                <p className="mt-4 max-w-2xl text-sm leading-7 text-slate-600 sm:text-base">
                                    This surface is wired directly to the hardened Super Admin commission APIs, with backend-backed rule precedence, snapshot safety, and live cache-aware resolution previews.
                                </p>
                            </div>

                            <div className="grid gap-3 sm:grid-cols-2 xl:min-w-[420px]">
                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center gap-2 rounded-3xl bg-slate-950 px-5 py-4 text-sm font-semibold text-white shadow-lg shadow-slate-900/10 transition hover:bg-slate-800"
                                    onClick={() => openCreateModal('Category')}
                                >
                                    <Sparkles className="h-4 w-4" />
                                    New Override Rule
                                </button>
                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center gap-2 rounded-3xl border border-emerald-200 bg-emerald-50 px-5 py-4 text-sm font-semibold text-emerald-900 transition hover:bg-emerald-100"
                                    onClick={() => {
                                        if (activeGlobalRule) {
                                            openEditModal(activeGlobalRule)
                                            return
                                        }

                                        openCreateModal('Global')
                                    }}
                                >
                                    <ShieldCheck className="h-4 w-4" />
                                    {activeGlobalRule ? 'Edit Global Rule' : 'Create Global Rule'}
                                </button>
                                <div className="rounded-3xl border border-slate-200 bg-white px-5 py-4 shadow-sm">
                                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                                        Active Rules
                                    </p>
                                    <p className="mt-2 text-lg font-semibold text-slate-950">
                                        {rules.filter((rule) => rule.isActive).length}
                                    </p>
                                </div>
                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center gap-2 rounded-3xl border border-slate-200 bg-white px-5 py-4 text-sm font-semibold text-slate-700 shadow-sm transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-70"
                                    disabled={isRefreshing}
                                    onClick={() => refreshAll()}
                                >
                                    <RefreshCw className={`h-4 w-4 ${isRefreshing ? 'animate-spin' : ''}`} />
                                    {isRefreshing ? 'Refreshing' : 'Refresh'}
                                </button>
                            </div>
                        </div>
                    </div>

                    <div className="grid gap-4 border-t border-slate-100 bg-slate-50/80 px-6 py-5 text-sm text-slate-600 sm:grid-cols-3 sm:px-8">
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                                API Timestamp
                            </p>
                            <p className="mt-2 font-medium text-slate-900">
                                {formatTimestamp(rulesQuery.data?.meta?.timestamp)}
                            </p>
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                                Request ID
                            </p>
                            <p className="mt-2 truncate font-mono text-xs text-slate-700">
                                {rulesQuery.data?.meta?.requestId ?? 'Pending'}
                            </p>
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                                Client Sync
                            </p>
                            <p className="mt-2 font-medium text-slate-900">
                                {rulesQuery.dataUpdatedAt
                                    ? new Date(rulesQuery.dataUpdatedAt).toLocaleTimeString()
                                    : 'Not yet synced'}
                            </p>
                        </div>
                    </div>
                </section>

                <div className="grid gap-6 xl:grid-cols-[0.9fr_1.1fr]">
                    <section className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                        <div className="border-b border-slate-100 px-6 py-5">
                            <p className="text-xs font-semibold uppercase tracking-[0.24em] text-slate-500">
                                Global Commission
                            </p>
                            <h2 className="mt-2 text-2xl font-semibold text-slate-950">
                                Default platform rate
                            </h2>
                            <p className="mt-2 text-sm leading-6 text-slate-500">
                                This rule applies when no vendor or category override matches the incoming order context.
                            </p>
                        </div>

                        <div className="space-y-5 px-6 py-6">
                            {activeGlobalRule ? (
                                <>
                                    <div className="rounded-[28px] border border-emerald-200 bg-emerald-50 p-5">
                                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-emerald-700">
                                            Active Global Rate
                                        </p>
                                        <p className="mt-3 text-4xl font-semibold text-emerald-950">
                                            {formatPercent(activeGlobalRule.rate)}
                                        </p>
                                        <div className="mt-4 grid gap-3 sm:grid-cols-2">
                                            <div className="rounded-2xl border border-white/80 bg-white px-4 py-3 shadow-sm">
                                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                    Last Updated
                                                </p>
                                                <p className="mt-2 text-sm font-semibold text-slate-900">
                                                    {formatTimestamp(activeGlobalRule.updatedAt ?? activeGlobalRule.createdAt)}
                                                </p>
                                            </div>
                                            <div className="rounded-2xl border border-white/80 bg-white px-4 py-3 shadow-sm">
                                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                    Priority
                                                </p>
                                                <p className="mt-2 text-sm font-semibold text-slate-900">
                                                    {activeGlobalRule.priority}
                                                </p>
                                            </div>
                                        </div>
                                    </div>

                                    <div className="rounded-[28px] border border-slate-200 bg-slate-50 px-5 py-4">
                                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                                            Active Rule ID
                                        </p>
                                        <p className="mt-2 break-all font-mono text-xs text-slate-700">
                                            {activeGlobalRule.id}
                                        </p>
                                    </div>

                                    <button
                                        type="button"
                                        className="inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white shadow-lg shadow-slate-900/15 transition hover:bg-slate-800"
                                        onClick={() => openEditModal(activeGlobalRule)}
                                    >
                                        <Layers3 className="h-4 w-4" />
                                        Edit global rate
                                    </button>
                                </>
                            ) : (
                                <div className="space-y-5 rounded-[28px] border border-amber-200 bg-amber-50 p-5">
                                    <div className="flex items-start gap-3">
                                        <div className="mt-0.5 flex h-10 w-10 items-center justify-center rounded-2xl bg-amber-100 text-amber-700">
                                            <ShieldAlert className="h-5 w-5" />
                                        </div>
                                        <div>
                                            <h3 className="text-lg font-semibold text-amber-950">
                                                No active global rule configured
                                            </h3>
                                            <p className="mt-2 text-sm leading-6 text-amber-900/80">
                                                The backend will fall back to the legacy static commission configuration until a global rule is created.
                                            </p>
                                        </div>
                                    </div>

                                    <button
                                        type="button"
                                        className="inline-flex items-center gap-2 rounded-2xl bg-amber-900 px-5 py-3 text-sm font-semibold text-white transition hover:bg-amber-950"
                                        onClick={() => openCreateModal('Global')}
                                    >
                                        <ShieldCheck className="h-4 w-4" />
                                        Create global rule
                                    </button>
                                </div>
                            )}
                        </div>
                    </section>

                    <CommissionPreviewPanel
                        categories={categories}
                        vendors={vendors}
                        errorMessage={previewMutation.isError ? getErrorMessage(previewMutation.error, 'Unable to preview commission resolution.') : null}
                        getAppliedRuleLabel={(result) => {
                            if (result.usedLegacyFallback) {
                                return formatAppliedRuleLabel('Legacy')
                            }

                            const scopeType =
                                result.scopeType ??
                                (result.source === 'Vendor' || result.source === 'Category' || result.source === 'Global'
                                    ? result.source
                                    : null)

                            if (!scopeType) {
                                return 'Applied Rule: Unavailable'
                            }

                            const scopeName =
                                scopeType === 'Category'
                                    ? categoryNamesById.get(result.scopeId ?? '')
                                    : scopeType === 'Vendor'
                                        ? vendorNamesById.get(result.scopeId ?? '')
                                        : null

                            return formatAppliedRuleLabel(scopeType, scopeName)
                        }}
                        isLoadingLookups={categoriesQuery.isLoading || vendorsQuery.isLoading}
                        isSubmitting={previewMutation.isPending}
                        result={previewMutation.data}
                        onPreview={handlePreview}
                    />
                </div>

                {(categoriesQuery.isError || vendorsQuery.isError) ? (
                    <div className="rounded-[28px] border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900 shadow-sm">
                        Scope names could not be fully hydrated from lookup endpoints, so the table falls back to stored IDs where necessary.
                    </div>
                ) : null}

                <CommissionTable
                    rules={rules}
                    isLoading={rulesQuery.isLoading}
                    activeActionRuleId={deactivateMutation.isPending ? pendingDeactivation?.id ?? null : null}
                    getAppliedRuleLabel={getRuleLabel}
                    getDeactivationState={getDeactivationState}
                    onEdit={openEditModal}
                    onDeactivate={setPendingDeactivation}
                    onCreateFirstRule={() => openCreateModal('Global')}
                    resolveScopeName={resolveScopeName}
                />
            </div>

            <CommissionFormModal
                isOpen={isFormOpen}
                isSubmitting={upsertMutation.isPending}
                rule={editingRule}
                defaultScopeType={defaultScopeType}
                categories={categories}
                vendors={vendors}
                onClose={closeFormModal}
                onSubmit={handleSubmitRule}
            />

            {pendingDeactivation ? (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/55 px-4 py-6 backdrop-blur-sm">
                    <div className="w-full max-w-lg overflow-hidden rounded-[32px] border border-white/10 bg-white shadow-[0_40px_120px_-52px_rgba(15,23,42,0.75)]">
                        <div className="border-b border-slate-100 px-6 py-5">
                            <p className="text-xs font-semibold uppercase tracking-[0.24em] text-rose-600">
                                Confirm Deactivation
                            </p>
                            <h2 className="mt-2 text-2xl font-semibold text-slate-950">
                                Deactivate this commission rule?
                            </h2>
                            <p className="mt-2 text-sm leading-6 text-slate-500">
                                The rule will remain visible for audit purposes, but it will no longer participate in future commission resolution.
                            </p>
                        </div>

                        <div className="space-y-4 px-6 py-6">
                            <div className="rounded-[28px] border border-slate-200 bg-slate-50 px-5 py-4">
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                                    Target
                                </p>
                                <p className="mt-2 text-sm font-semibold text-slate-900">
                                    {pendingDeactivation.scopeType} - {resolveScopeName(pendingDeactivation)}
                                </p>
                                <p className="mt-1 text-sm text-slate-600">
                                    {formatPercent(pendingDeactivation.rate)} at priority {pendingDeactivation.priority}
                                </p>
                                {pendingDeactivation.scopeType === 'Global' && activeGlobalRuleCount === 1 ? (
                                    <p className="mt-3 rounded-2xl border border-amber-200 bg-amber-50 px-3 py-2 text-xs font-medium text-amber-900">
                                        You must always have one active global commission rule.
                                    </p>
                                ) : null}
                            </div>

                            <div className="flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center rounded-2xl border border-slate-200 bg-white px-5 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                    onClick={() => setPendingDeactivation(null)}
                                >
                                    Cancel
                                </button>
                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center rounded-2xl bg-rose-600 px-5 py-3 text-sm font-semibold text-white transition hover:bg-rose-700 disabled:cursor-not-allowed disabled:opacity-60"
                                    disabled={deactivateMutation.isPending}
                                    onClick={() => handleConfirmDeactivate()}
                                >
                                    {deactivateMutation.isPending ? 'Deactivating...' : 'Deactivate Rule'}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            ) : null}
        </>
    )
}

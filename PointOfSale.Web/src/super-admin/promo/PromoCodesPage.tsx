import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { RefreshCw, ShieldAlert, Sparkles, TicketPercent } from 'lucide-react'
import { useToast } from '../../components/Toast'
import {
    activatePromoCode,
    createPromoCode,
    deactivatePromoCode,
    getPromoCategories,
    getPromoCodes,
    getPromoVendors,
    previewPromoCode,
    updatePromoCode,
    type PromoCategoryOption,
    type PromoCodeFormInput,
    type PromoDiscountType,
    type PromoScopeType,
    type PromoVendorOption,
    type SuperAdminPromoCode,
} from './promoService'

const promoCodesQueryKey = ['super-admin', 'promo-codes'] as const
const promoCategoriesQueryKey = ['super-admin', 'promo-codes', 'categories'] as const
const promoVendorsQueryKey = ['super-admin', 'promo-codes', 'vendors'] as const

type PromoFormState = {
    code: string
    discountType: PromoDiscountType
    discountValue: string
    maxDiscountAmount: string
    usageLimit: string
    maxUsagePerCustomer: string
    expiryDate: string
    isActive: boolean
    scopeType: PromoScopeType
    scopeId: string
}

function formatCurrency(amount: number, currency = 'EGP'): string {
    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency,
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(Number(amount || 0))
}

function formatDateTime(value?: string | null): string {
    if (!value) {
        return 'No expiry'
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return 'Unavailable'
    }

    return date.toLocaleString()
}

function getErrorMessage(error: unknown, fallback: string): string {
    return error instanceof Error && error.message ? error.message : fallback
}

function getPromoScopeType(promo: SuperAdminPromoCode): PromoScopeType {
    if (promo.vendorId) {
        return 'Vendor'
    }

    if (promo.categoryId) {
        return 'Category'
    }

    return 'Global'
}

function getPromoScopeLabel(
    promo: SuperAdminPromoCode,
    categoriesById: Map<string, string>,
    vendorsById: Map<string, string>,
): string {
    if (promo.vendorId) {
        return `Vendor - ${vendorsById.get(promo.vendorId) ?? promo.vendorId}`
    }

    if (promo.categoryId) {
        return `Category - ${categoriesById.get(promo.categoryId) ?? promo.categoryId}`
    }

    return 'Global'
}

function getPromoHealth(promo: SuperAdminPromoCode): { label: string; tone: string } {
    const expiryTime = promo.expiryDate ? new Date(promo.expiryDate).getTime() : Number.NaN
    const isExpired = Number.isFinite(expiryTime) && expiryTime <= Date.now()
    const isUsageExhausted =
        typeof promo.usageLimit === 'number' && promo.usageLimit > 0 && promo.usedCount >= promo.usageLimit

    if (!promo.isActive) {
        return { label: 'Inactive', tone: 'border-slate-200 bg-slate-50 text-slate-700' }
    }

    if (isExpired) {
        return { label: 'Expired', tone: 'border-rose-200 bg-rose-50 text-rose-700' }
    }

    if (isUsageExhausted) {
        return { label: 'Limit Reached', tone: 'border-amber-200 bg-amber-50 text-amber-800' }
    }

    return { label: 'Active', tone: 'border-emerald-200 bg-emerald-50 text-emerald-800' }
}

function toDateTimeLocalInput(value?: string | null): string {
    if (!value) {
        return ''
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return ''
    }

    const offsetMinutes = date.getTimezoneOffset()
    const local = new Date(date.getTime() - offsetMinutes * 60_000)
    return local.toISOString().slice(0, 16)
}

function toIsoStringOrNull(value: string): string | null {
    if (!value.trim()) {
        return null
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return null
    }

    return date.toISOString()
}

function buildFormState(promo: SuperAdminPromoCode | null): PromoFormState {
    if (!promo) {
        return {
            code: '',
            discountType: 'Percentage',
            discountValue: '',
            maxDiscountAmount: '',
            usageLimit: '',
            maxUsagePerCustomer: '',
            expiryDate: '',
            isActive: true,
            scopeType: 'Global',
            scopeId: '',
        }
    }

    return {
        code: promo.code,
        discountType: promo.discountType,
        discountValue: String(promo.discountValue),
        maxDiscountAmount: typeof promo.maxDiscountAmount === 'number' ? String(promo.maxDiscountAmount) : '',
        usageLimit: typeof promo.usageLimit === 'number' ? String(promo.usageLimit) : '',
        maxUsagePerCustomer: typeof promo.maxUsagePerCustomer === 'number' ? String(promo.maxUsagePerCustomer) : '',
        expiryDate: toDateTimeLocalInput(promo.expiryDate),
        isActive: promo.isActive,
        scopeType: getPromoScopeType(promo),
        scopeId: promo.vendorId ?? promo.categoryId ?? '',
    }
}

function PromoCodeFormModal({
    isOpen,
    isSubmitting,
    promo,
    categories,
    vendors,
    onClose,
    onSubmit,
}: {
    isOpen: boolean
    isSubmitting: boolean
    promo: SuperAdminPromoCode | null
    categories: PromoCategoryOption[]
    vendors: PromoVendorOption[]
    onClose: () => void
    onSubmit: (input: PromoCodeFormInput) => Promise<void>
}) {
    const [form, setForm] = useState<PromoFormState>(() => buildFormState(promo))
    const [formError, setFormError] = useState<string | null>(null)

    useEffect(() => {
        if (!isOpen) {
            return
        }

        setForm(buildFormState(promo))
        setFormError(null)
    }, [isOpen, promo])

    if (!isOpen) {
        return null
    }

    const scopeOptions = form.scopeType === 'Vendor' ? vendors : categories

    async function handleSubmit() {
        const normalizedCode = form.code.trim().toUpperCase()
        const discountValue = Number(form.discountValue)
        const maxDiscountAmount = form.maxDiscountAmount.trim() ? Number(form.maxDiscountAmount) : null
        const usageLimit = form.usageLimit.trim() ? Number(form.usageLimit) : null
        const maxUsagePerCustomer = form.maxUsagePerCustomer.trim() ? Number(form.maxUsagePerCustomer) : null

        if (!normalizedCode) {
            setFormError('Promo code is required.')
            return
        }

        if (!Number.isFinite(discountValue) || discountValue <= 0) {
            setFormError('Discount value must be greater than zero.')
            return
        }

        if (form.discountType === 'Percentage' && discountValue > 100) {
            setFormError('Percentage discount cannot exceed 100%.')
            return
        }

        if (maxDiscountAmount !== null && (!Number.isFinite(maxDiscountAmount) || maxDiscountAmount <= 0)) {
            setFormError('Max discount must be greater than zero when provided.')
            return
        }

        if (usageLimit !== null && (!Number.isFinite(usageLimit) || usageLimit <= 0)) {
            setFormError('Usage limit must be greater than zero when provided.')
            return
        }

        if (maxUsagePerCustomer !== null && (!Number.isFinite(maxUsagePerCustomer) || maxUsagePerCustomer <= 0)) {
            setFormError('Per-customer usage limit must be greater than zero when provided.')
            return
        }

        if (usageLimit !== null && maxUsagePerCustomer !== null && maxUsagePerCustomer > usageLimit) {
            setFormError('Per-customer usage limit cannot exceed the total usage limit.')
            return
        }

        if (form.scopeType !== 'Global' && !form.scopeId) {
            setFormError(`Select a ${form.scopeType.toLowerCase()} target.`)
            return
        }

        setFormError(null)

        await onSubmit({
            code: normalizedCode,
            discountType: form.discountType,
            discountValue,
            maxDiscountAmount,
            usageLimit: usageLimit === null ? null : Math.floor(usageLimit),
            maxUsagePerCustomer: maxUsagePerCustomer === null ? null : Math.floor(maxUsagePerCustomer),
            expiryDate: toIsoStringOrNull(form.expiryDate),
            isActive: form.isActive,
            scopeType: form.scopeType,
            scopeId: form.scopeType === 'Global' ? null : form.scopeId,
        })
    }

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/55 px-4 py-6 backdrop-blur-sm">
            <div className="w-full max-w-3xl overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_40px_120px_-52px_rgba(15,23,42,0.75)]">
                <div className="border-b border-slate-100 px-6 py-5">
                    <p className="text-xs font-semibold uppercase tracking-[0.24em] text-emerald-600">
                        {promo ? 'Edit Promo Code' : 'Create Promo Code'}
                    </p>
                    <h2 className="mt-2 text-2xl font-semibold text-slate-950">
                        {promo ? `Update ${promo.code}` : 'Configure a commission-only promo'}
                    </h2>
                </div>

                <div className="space-y-5 px-6 py-6">
                    <div className="grid gap-4 md:grid-cols-2">
                        <input
                            value={form.code}
                            onChange={(event) => setForm((current) => ({ ...current, code: event.target.value.toUpperCase() }))}
                            placeholder="Promo code"
                            className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm uppercase outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                        />
                        <select
                            value={form.discountType}
                            onChange={(event) => setForm((current) => ({ ...current, discountType: event.target.value as PromoDiscountType }))}
                            className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                        >
                            <option value="Percentage">Percentage</option>
                            <option value="Fixed">Fixed amount</option>
                        </select>
                        <input
                            value={form.discountValue}
                            onChange={(event) => setForm((current) => ({ ...current, discountValue: event.target.value }))}
                            type="number"
                            min="0"
                            step="0.01"
                            placeholder={form.discountType === 'Percentage' ? 'Discount percent' : 'Discount amount'}
                            className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                        />
                        <input
                            value={form.maxDiscountAmount}
                            onChange={(event) => setForm((current) => ({ ...current, maxDiscountAmount: event.target.value }))}
                            type="number"
                            min="0"
                            step="0.01"
                            placeholder="Max discount amount"
                            className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                        />
                        <input
                            value={form.usageLimit}
                            onChange={(event) => setForm((current) => ({ ...current, usageLimit: event.target.value }))}
                            type="number"
                            min="1"
                            step="1"
                            placeholder="Usage limit"
                            className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                        />
                        <input
                            value={form.maxUsagePerCustomer}
                            onChange={(event) => setForm((current) => ({ ...current, maxUsagePerCustomer: event.target.value }))}
                            type="number"
                            min="1"
                            step="1"
                            placeholder="Per-customer limit"
                            className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                        />
                        <input
                            value={form.expiryDate}
                            onChange={(event) => setForm((current) => ({ ...current, expiryDate: event.target.value }))}
                            type="datetime-local"
                            className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                        />
                    </div>

                    <div className="grid gap-4 md:grid-cols-[0.45fr_1fr]">
                        <select
                            value={form.scopeType}
                            onChange={(event) => setForm((current) => ({ ...current, scopeType: event.target.value as PromoScopeType, scopeId: '' }))}
                            className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                        >
                            <option value="Global">Global</option>
                            <option value="Vendor">Vendor</option>
                            <option value="Category">Category</option>
                        </select>

                        {form.scopeType === 'Global' ? (
                            <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-500">
                                Applies to every eligible commission line in CommerceHub.
                            </div>
                        ) : (
                            <select
                                value={form.scopeId}
                                onChange={(event) => setForm((current) => ({ ...current, scopeId: event.target.value }))}
                                className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                            >
                                <option value="">{form.scopeType === 'Vendor' ? 'Select vendor' : 'Select category'}</option>
                                {scopeOptions.map((option) => (
                                    <option key={option.id} value={option.id}>
                                        {option.name}
                                    </option>
                                ))}
                            </select>
                        )}
                    </div>

                    <label className="inline-flex items-center gap-3 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 shadow-sm">
                        <input
                            type="checkbox"
                            checked={form.isActive}
                            onChange={(event) => setForm((current) => ({ ...current, isActive: event.target.checked }))}
                            className="h-4 w-4 rounded border-slate-300"
                        />
                        Active immediately
                    </label>

                    {formError ? (
                        <div className="rounded-[24px] border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-900">
                            {formError}
                        </div>
                    ) : null}
                </div>

                <div className="flex flex-col-reverse gap-3 border-t border-slate-100 px-6 py-5 sm:flex-row sm:justify-end">
                    <button
                        type="button"
                        className="inline-flex items-center justify-center rounded-2xl border border-slate-200 bg-white px-5 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                        onClick={onClose}
                    >
                        Cancel
                    </button>
                    <button
                        type="button"
                        className="inline-flex items-center justify-center rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                        disabled={isSubmitting}
                        onClick={() => handleSubmit()}
                    >
                        {isSubmitting ? 'Saving...' : promo ? 'Save Changes' : 'Create Promo'}
                    </button>
                </div>
            </div>
        </div>
    )
}

export default function PromoCodesPage() {
    const queryClient = useQueryClient()
    const { showError, showSuccess } = useToast()
    const [isFormOpen, setIsFormOpen] = useState(false)
    const [editingPromo, setEditingPromo] = useState<SuperAdminPromoCode | null>(null)
    const [previewPromoId, setPreviewPromoId] = useState('')
    const [previewBaseCommission, setPreviewBaseCommission] = useState('100')
    const [previewVendorId, setPreviewVendorId] = useState('')
    const [previewCategoryId, setPreviewCategoryId] = useState('')

    const promoCodesQuery = useQuery({
        queryKey: promoCodesQueryKey,
        queryFn: getPromoCodes,
        staleTime: 15_000,
        refetchOnWindowFocus: true,
    })

    const categoriesQuery = useQuery({
        queryKey: promoCategoriesQueryKey,
        queryFn: getPromoCategories,
        staleTime: 5 * 60_000,
    })

    const vendorsQuery = useQuery({
        queryKey: promoVendorsQueryKey,
        queryFn: getPromoVendors,
        staleTime: 5 * 60_000,
    })

    const upsertMutation = useMutation({
        mutationFn: async ({
            promoId,
            input,
        }: {
            promoId?: string
            input: PromoCodeFormInput
        }) => (promoId ? updatePromoCode(promoId, input) : createPromoCode(input)),
    })

    const toggleMutation = useMutation({
        mutationFn: async (promo: SuperAdminPromoCode) =>
            promo.isActive ? deactivatePromoCode(promo.id) : activatePromoCode(promo.id),
    })

    const previewMutation = useMutation({
        mutationFn: () =>
            previewPromoCode({
                promoId: previewPromoId || null,
                baseCommissionAmount: Number(previewBaseCommission),
                previewVendorId: previewVendorId || null,
                previewCategoryId: previewCategoryId || null,
            }),
    })

    const promoCodes = promoCodesQuery.data?.promoCodes ?? []
    const categories = categoriesQuery.data ?? []
    const vendors = vendorsQuery.data ?? []

    const categoriesById = useMemo(
        () =>
            new Map(
                categories.map((category) => [
                    category.id,
                    `${category.iconEmoji ? `${category.iconEmoji} ` : ''}${category.name}`,
                ]),
            ),
        [categories],
    )

    const vendorsById = useMemo(
        () => new Map(vendors.map((vendor) => [vendor.id, vendor.name])),
        [vendors],
    )

    const selectedPreviewPromo = useMemo(
        () => promoCodes.find((promo) => promo.id === previewPromoId) ?? null,
        [promoCodes, previewPromoId],
    )

    const activeCount = promoCodes.filter((promo) => getPromoHealth(promo).label === 'Active').length
    const totalUsageCount = promoCodes.reduce((sum, promo) => sum + promo.usedCount, 0)
    const scopedCount = promoCodes.filter((promo) => getPromoScopeType(promo) !== 'Global').length
    const inactiveCount = promoCodes.filter((promo) => getPromoHealth(promo).label !== 'Active').length

    async function refreshAll() {
        await Promise.all([
            promoCodesQuery.refetch(),
            categoriesQuery.refetch(),
            vendorsQuery.refetch(),
        ])
    }

    async function handleSubmitPromo(input: PromoCodeFormInput) {
        try {
            await upsertMutation.mutateAsync({
                promoId: editingPromo?.id,
                input,
            })

            await queryClient.invalidateQueries({ queryKey: promoCodesQueryKey })
            showSuccess(editingPromo ? 'Promo code updated successfully.' : 'Promo code created successfully.')
            setIsFormOpen(false)
            setEditingPromo(null)
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to save the promo code.'))
        }
    }

    async function handleTogglePromo(promo: SuperAdminPromoCode) {
        try {
            await toggleMutation.mutateAsync(promo)
            await queryClient.invalidateQueries({ queryKey: promoCodesQueryKey })
            showSuccess(promo.isActive ? 'Promo code deactivated.' : 'Promo code activated.')
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to update promo status.'))
        }
    }

    async function handlePreview() {
        if (!previewPromoId) {
            showError('Select a promo code to preview.')
            return
        }

        const baseCommissionAmount = Number(previewBaseCommission)
        if (!Number.isFinite(baseCommissionAmount) || baseCommissionAmount < 0) {
            showError('Base commission must be zero or greater.')
            return
        }

        try {
            await previewMutation.mutateAsync()
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to preview the promo calculation.'))
        }
    }

    if (promoCodesQuery.isError && !promoCodesQuery.data) {
        return (
            <div className="grid min-h-[60vh] place-items-center">
                <div className="w-full max-w-xl rounded-[32px] border border-slate-200 bg-white p-8 text-center shadow-[0_30px_80px_-48px_rgba(15,23,42,0.45)]">
                    <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                        <ShieldAlert className="h-6 w-6" />
                    </div>
                    <h2 className="mt-5 text-2xl font-semibold text-slate-950">Promo controls unavailable</h2>
                    <p className="mt-3 text-sm leading-6 text-slate-500">
                        {getErrorMessage(promoCodesQuery.error, 'Unable to load CommerceHub promo codes.')}
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

    return (
        <>
            <div className="space-y-6">
                <section className="overflow-hidden rounded-[36px] border border-slate-200 bg-white shadow-[0_36px_100px_-56px_rgba(15,23,42,0.45)]">
                    <div className="relative overflow-hidden px-6 py-6 sm:px-8 sm:py-8">
                        <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(16,185,129,0.18),_transparent_28%),radial-gradient(circle_at_bottom_right,_rgba(59,130,246,0.12),_transparent_26%)]" />
                        <div className="relative flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
                            <div className="max-w-3xl">
                                <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800">
                                    <TicketPercent className="h-4 w-4" />
                                    Promo Codes
                                </div>
                                <h1 className="mt-5 text-3xl font-semibold tracking-tight text-slate-950 sm:text-4xl">
                                    Reduce platform commission without touching vendor earnings
                                </h1>
                                <p className="mt-4 max-w-2xl text-sm leading-7 text-slate-600 sm:text-base">
                                    Create global, vendor, or category scoped promos that discount platform commission only while leaving order totals and vendor payouts unchanged.
                                </p>
                            </div>

                            <div className="grid gap-3 sm:grid-cols-2 xl:min-w-[420px]">
                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center gap-2 rounded-3xl bg-slate-950 px-5 py-4 text-sm font-semibold text-white shadow-lg shadow-slate-900/10 transition hover:bg-slate-800"
                                    onClick={() => {
                                        setEditingPromo(null)
                                        setIsFormOpen(true)
                                    }}
                                >
                                    <Sparkles className="h-4 w-4" />
                                    New Promo Code
                                </button>
                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center gap-2 rounded-3xl border border-slate-200 bg-white px-5 py-4 text-sm font-semibold text-slate-700 shadow-sm transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-70"
                                    disabled={promoCodesQuery.isFetching || categoriesQuery.isFetching || vendorsQuery.isFetching}
                                    onClick={() => refreshAll()}
                                >
                                    <RefreshCw className={`h-4 w-4 ${promoCodesQuery.isFetching || categoriesQuery.isFetching || vendorsQuery.isFetching ? 'animate-spin' : ''}`} />
                                    Refresh
                                </button>
                                <div className="rounded-3xl border border-slate-200 bg-white px-5 py-4 shadow-sm">
                                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Active Promos</p>
                                    <p className="mt-2 text-lg font-semibold text-slate-950">{activeCount}</p>
                                </div>
                                <div className="rounded-3xl border border-emerald-200 bg-emerald-50 px-5 py-4 shadow-sm">
                                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-emerald-700">Total Uses</p>
                                    <p className="mt-2 text-lg font-semibold text-emerald-950">{totalUsageCount}</p>
                                </div>
                            </div>
                        </div>
                    </div>

                    <div className="grid gap-4 border-t border-slate-100 bg-slate-50/80 px-6 py-5 text-sm text-slate-600 sm:grid-cols-3 sm:px-8">
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Scoped Promos</p>
                            <p className="mt-2 font-medium text-slate-900">{scopedCount}</p>
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Inactive or Expired</p>
                            <p className="mt-2 font-medium text-slate-900">{inactiveCount}</p>
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">API Timestamp</p>
                            <p className="mt-2 font-medium text-slate-900">{formatDateTime(promoCodesQuery.data?.meta?.timestamp)}</p>
                        </div>
                    </div>
                </section>

                <div className="grid gap-6 xl:grid-cols-[0.95fr_1.05fr]">
                    <section className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-slate-500">Preview</p>
                        <h2 className="mt-2 text-2xl font-semibold text-slate-950">Simulate commission impact</h2>
                        <div className="mt-5 space-y-4">
                            <select
                                value={previewPromoId}
                                onChange={(event) => setPreviewPromoId(event.target.value)}
                                className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                            >
                                <option value="">Select a promo code</option>
                                {promoCodes.map((promo) => (
                                    <option key={promo.id} value={promo.id}>
                                        {promo.code} - {getPromoScopeLabel(promo, categoriesById, vendorsById)}
                                    </option>
                                ))}
                            </select>

                            <div className="grid gap-4 md:grid-cols-3">
                                <input
                                    value={previewBaseCommission}
                                    onChange={(event) => setPreviewBaseCommission(event.target.value)}
                                    type="number"
                                    min="0"
                                    step="0.01"
                                    placeholder="Base commission"
                                    className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                />
                                <select
                                    value={previewVendorId}
                                    onChange={(event) => setPreviewVendorId(event.target.value)}
                                    className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                >
                                    <option value="">Any vendor</option>
                                    {vendors.map((vendor) => (
                                        <option key={vendor.id} value={vendor.id}>
                                            {vendor.name}
                                        </option>
                                    ))}
                                </select>
                                <select
                                    value={previewCategoryId}
                                    onChange={(event) => setPreviewCategoryId(event.target.value)}
                                    className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                >
                                    <option value="">Any category</option>
                                    {categories.map((category) => (
                                        <option key={category.id} value={category.id}>
                                            {`${category.iconEmoji ? `${category.iconEmoji} ` : ''}${category.name}`}
                                        </option>
                                    ))}
                                </select>
                            </div>

                            <button
                                type="button"
                                className="inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                                disabled={previewMutation.isPending}
                                onClick={() => handlePreview()}
                            >
                                {previewMutation.isPending ? 'Calculating...' : 'Preview Discount'}
                            </button>

                            {selectedPreviewPromo ? (
                                <div className="rounded-[24px] border border-slate-200 bg-slate-50 px-4 py-4 text-sm text-slate-600">
                                    <p className="font-semibold text-slate-900">
                                        {selectedPreviewPromo.code} applies to {getPromoScopeLabel(selectedPreviewPromo, categoriesById, vendorsById)}
                                    </p>
                                </div>
                            ) : null}

                            {previewMutation.data ? (
                                <div className="grid gap-3 rounded-[28px] border border-emerald-200 bg-emerald-50 p-4 sm:grid-cols-3">
                                    <div className="rounded-2xl border border-white/80 bg-white px-4 py-3 shadow-sm">
                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Base</p>
                                        <p className="mt-2 text-lg font-semibold text-slate-950">
                                            {formatCurrency(previewMutation.data.baseCommissionAmount)}
                                        </p>
                                    </div>
                                    <div className="rounded-2xl border border-white/80 bg-white px-4 py-3 shadow-sm">
                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Discount</p>
                                        <p className="mt-2 text-lg font-semibold text-emerald-700">
                                            {formatCurrency(previewMutation.data.discountAmount)}
                                        </p>
                                    </div>
                                    <div className="rounded-2xl border border-white/80 bg-white px-4 py-3 shadow-sm">
                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Final</p>
                                        <p className="mt-2 text-lg font-semibold text-slate-950">
                                            {formatCurrency(previewMutation.data.finalCommissionAmount)}
                                        </p>
                                    </div>
                                </div>
                            ) : null}
                        </div>
                    </section>

                    <section className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-slate-500">Guardrails</p>
                        <h2 className="mt-2 text-2xl font-semibold text-slate-950">Platform-side protection</h2>
                        <div className="mt-5 space-y-4">
                            <div className="rounded-[24px] border border-emerald-200 bg-emerald-50 px-4 py-4 text-sm text-emerald-950">
                                <p className="font-semibold">Product totals never change.</p>
                                <p className="mt-1">Promos reduce only platform commission, so customer totals and vendor payouts stay intact.</p>
                            </div>
                            <div className="rounded-[24px] border border-slate-200 bg-slate-50 px-4 py-4 text-sm text-slate-700">
                                <p className="font-semibold text-slate-950">Scope-aware application.</p>
                                <p className="mt-1">Global, vendor, and category scope let CommerceHub target commission reductions precisely.</p>
                            </div>
                            <div className="rounded-[24px] border border-slate-200 bg-slate-50 px-4 py-4 text-sm text-slate-700">
                                <p className="font-semibold text-slate-950">Audit-ready controls.</p>
                                <p className="mt-1">Usage counts, expiry, and activation state remain visible for financial review and operational tracing.</p>
                            </div>
                        </div>
                    </section>
                </div>

                <section className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                    <div className="border-b border-slate-100 px-6 py-5">
                        <h2 className="text-2xl font-semibold text-slate-950">Promo inventory</h2>
                    </div>

                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-slate-100">
                            <thead className="bg-slate-50/90">
                                <tr className="text-left text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">
                                    <th className="px-6 py-4">Code</th>
                                    <th className="px-6 py-4">Scope</th>
                                    <th className="px-6 py-4">Discount</th>
                                    <th className="px-6 py-4">Usage</th>
                                    <th className="px-6 py-4">Expiry</th>
                                    <th className="px-6 py-4">Status</th>
                                    <th className="px-6 py-4 text-right">Actions</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-slate-100">
                                {promoCodes.length === 0 ? (
                                    <tr>
                                        <td colSpan={7} className="px-6 py-10 text-center text-sm text-slate-500">
                                            No promo codes have been created yet.
                                        </td>
                                    </tr>
                                ) : (
                                    promoCodes.map((promo) => {
                                        const health = getPromoHealth(promo)

                                        return (
                                            <tr key={promo.id}>
                                                <td className="px-6 py-4">
                                                    <p className="font-semibold text-slate-950">{promo.code}</p>
                                                    <p className="mt-1 text-xs text-slate-500">Created {formatDateTime(promo.createdAt)}</p>
                                                </td>
                                                <td className="px-6 py-4 text-sm text-slate-700">
                                                    {getPromoScopeLabel(promo, categoriesById, vendorsById)}
                                                </td>
                                                <td className="px-6 py-4 text-sm text-slate-700">
                                                    <p className="font-semibold text-slate-950">
                                                        {promo.discountType === 'Percentage'
                                                            ? `${promo.discountValue}%`
                                                            : formatCurrency(promo.discountValue)}
                                                    </p>
                                                    <p className="mt-1 text-xs text-slate-500">
                                                        {typeof promo.maxDiscountAmount === 'number'
                                                            ? `Max ${formatCurrency(promo.maxDiscountAmount)}`
                                                            : 'No cap'}
                                                    </p>
                                                </td>
                                                <td className="px-6 py-4 text-sm text-slate-700">
                                                    {promo.usedCount}
                                                    <span className="mt-1 block text-xs text-slate-500">
                                                        {typeof promo.usageLimit === 'number' ? `of ${promo.usageLimit}` : 'Unlimited'}
                                                    </span>
                                                    <span className="mt-1 block text-xs text-slate-500">
                                                        {typeof promo.maxUsagePerCustomer === 'number'
                                                            ? `${promo.maxUsagePerCustomer} per customer`
                                                            : 'No per-customer cap'}
                                                    </span>
                                                </td>
                                                <td className="px-6 py-4 text-sm text-slate-700">{formatDateTime(promo.expiryDate)}</td>
                                                <td className="px-6 py-4">
                                                    <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold ${health.tone}`}>
                                                        {health.label}
                                                    </span>
                                                </td>
                                                <td className="px-6 py-4">
                                                    <div className="flex justify-end gap-2">
                                                        <button
                                                            type="button"
                                                            className="rounded-2xl border border-slate-200 px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                                            onClick={() => {
                                                                setEditingPromo(promo)
                                                                setIsFormOpen(true)
                                                            }}
                                                        >
                                                            Edit
                                                        </button>
                                                        <button
                                                            type="button"
                                                            className={`rounded-2xl px-4 py-2 text-sm font-semibold text-white transition ${
                                                                promo.isActive ? 'bg-rose-600 hover:bg-rose-700' : 'bg-emerald-600 hover:bg-emerald-700'
                                                            }`}
                                                            disabled={toggleMutation.isPending}
                                                            onClick={() => handleTogglePromo(promo)}
                                                        >
                                                            {promo.isActive ? 'Deactivate' : 'Activate'}
                                                        </button>
                                                    </div>
                                                </td>
                                            </tr>
                                        )
                                    })
                                )}
                            </tbody>
                        </table>
                    </div>
                </section>
            </div>

            <PromoCodeFormModal
                isOpen={isFormOpen}
                isSubmitting={upsertMutation.isPending}
                promo={editingPromo}
                categories={categories}
                vendors={vendors}
                onClose={() => {
                    setIsFormOpen(false)
                    setEditingPromo(null)
                }}
                onSubmit={handleSubmitPromo}
            />
        </>
    )
}

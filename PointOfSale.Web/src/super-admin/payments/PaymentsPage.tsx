import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, CheckCircle2, CreditCard, Loader2, Search, ShieldCheck, Upload } from 'lucide-react'
import { useToast } from '../../components/Toast'
import { getSuperAdminOrderTenants } from '../orders/ordersService'
import {
    SUPER_ADMIN_VENDOR_PAYMENT_STATUSES,
    approveSuperAdminVendorPayment,
    createSuperAdminVendorPaymentMethod,
    getSuperAdminVendorPaymentMethods,
    getSuperAdminVendorPaymentProofUrl,
    getSuperAdminVendorPayments,
    rejectSuperAdminVendorPayment,
    type SuperAdminVendorPaymentFilters,
    type SuperAdminVendorPaymentMethod,
    type SuperAdminVendorPaymentRequestItem,
} from './paymentsService'
import { requireCurrentSuperAdminPaymentSelection } from './paymentOwnershipAuthority'

const PAGE_SIZE_OPTIONS = [20, 50, 100]

function formatCurrency(amount: number, currency = 'EGP') {
    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency,
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(Number(amount || 0))
}

function formatDateTime(value?: string | null) {
    if (!value) {
        return 'Pending'
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return 'Pending'
    }

    return date.toLocaleString()
}

function statusTone(status: string) {
    switch (status) {
        case 'Approved':
            return 'border-emerald-200 bg-emerald-50 text-emerald-900'
        case 'Rejected':
            return 'border-rose-200 bg-rose-50 text-rose-900'
        default:
            return 'border-amber-200 bg-amber-50 text-amber-900'
    }
}

function getErrorMessage(error: unknown, fallback: string) {
    return error instanceof Error && error.message ? error.message : fallback
}

interface ActionModalState {
    mode: 'approve' | 'reject'
    paymentRequestId: string
}

export default function PaymentsPage() {
    const queryClient = useQueryClient()
    const { showError, showSuccess } = useToast()
    const [filters, setFilters] = useState<SuperAdminVendorPaymentFilters>({
        page: 1,
        pageSize: 20,
        status: 'PendingVerification',
        vendorId: '',
        search: '',
    })
    const [methodForm, setMethodForm] = useState({
        name: '',
        destinationValue: '',
        instructions: '',
        isActive: true,
    })
    const [actionModal, setActionModal] = useState<ActionModalState | null>(null)
    const [actionReason, setActionReason] = useState('')
    const [allowOverpayment, setAllowOverpayment] = useState(false)

    const methodsQuery = useQuery({
        queryKey: ['super-admin-vendor-payment-methods'],
        queryFn: getSuperAdminVendorPaymentMethods,
    })

    const tenantsQuery = useQuery({
        queryKey: ['super-admin-vendor-payment-tenants'],
        queryFn: getSuperAdminOrderTenants,
        staleTime: 60_000,
    })

    const paymentsQuery = useQuery({
        queryKey: ['super-admin-vendor-payments', filters],
        queryFn: () => getSuperAdminVendorPayments(filters),
    })

    const createMethodMutation = useMutation({
        mutationFn: () => createSuperAdminVendorPaymentMethod(methodForm),
        onSuccess: () => {
            showSuccess('Payment method created.')
            setMethodForm({ name: '', destinationValue: '', instructions: '', isActive: true })
            queryClient.invalidateQueries({ queryKey: ['super-admin-vendor-payment-methods'] })
        },
        onError: (error) => showError(getErrorMessage(error, 'Unable to create payment method.')),
    })

    const approveMutation = useMutation({
        mutationFn: () =>
            approveSuperAdminVendorPayment(actionModal!.paymentRequestId, {
                reason: actionReason,
                allowOverpayment,
            }),
        onSuccess: () => {
            showSuccess('Payment request approved and posted to the ledger.')
            closeActionModal()
            queryClient.invalidateQueries({ queryKey: ['super-admin-vendor-payments'] })
        },
        onError: (error) => showError(getErrorMessage(error, 'Unable to approve the payment request.')),
    })

    const rejectMutation = useMutation({
        mutationFn: () =>
            rejectSuperAdminVendorPayment(actionModal!.paymentRequestId, {
                reason: actionReason,
            }),
        onSuccess: () => {
            showSuccess('Payment request rejected.')
            closeActionModal()
            queryClient.invalidateQueries({ queryKey: ['super-admin-vendor-payments'] })
        },
        onError: (error) => showError(getErrorMessage(error, 'Unable to reject the payment request.')),
    })

    const requests = paymentsQuery.isFetching || paymentsQuery.isError ? [] : paymentsQuery.data?.requests ?? []
    const currentActionRequest = requireCurrentSuperAdminPaymentSelection(actionModal?.paymentRequestId ?? null, requests)
    const meta = paymentsQuery.data?.meta
    const totalPages = Math.max(1, Number((meta as { totalPages?: number })?.totalPages || 1))
    const pendingCount = requests.filter((request) => request.status === 'PendingVerification').length
    const approvedCount = requests.filter((request) => request.status === 'Approved').length

    const isActionSubmitting = approveMutation.isPending || rejectMutation.isPending

    useEffect(() => {
        if (actionModal && !paymentsQuery.isFetching && !currentActionRequest) {
            closeActionModal()
        }
    }, [actionModal, currentActionRequest, paymentsQuery.isFetching])

    const methodCards = useMemo(
        () => methodsQuery.data ?? [],
        [methodsQuery.data],
    )

    function updateFilters(next: Partial<SuperAdminVendorPaymentFilters>) {
        setFilters((current) => ({
            ...current,
            ...next,
            page: next.page ?? (next.pageSize || next.status !== undefined || next.vendorId !== undefined || next.search !== undefined ? 1 : current.page),
        }))
    }

    function openActionModal(mode: 'approve' | 'reject', request: SuperAdminVendorPaymentRequestItem) {
        setActionModal({ mode, paymentRequestId: request.paymentRequestId })
        setActionReason('')
        setAllowOverpayment(false)
    }

    function closeActionModal() {
        setActionModal(null)
        setActionReason('')
        setAllowOverpayment(false)
    }

    async function handleOpenProof(paymentRequestId: string) {
        try {
            const proof = await getSuperAdminVendorPaymentProofUrl(paymentRequestId)
            window.open(proof.fileUrl, '_blank', 'noopener,noreferrer')
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to open payment proof.'))
        }
    }

    function handleActionConfirm() {
        if (!actionModal || !currentActionRequest || !actionReason.trim()) {
            showError('A reason is required before continuing.')
            return
        }

        if (actionModal.mode === 'approve') {
            approveMutation.mutate()
            return
        }

        rejectMutation.mutate()
    }

    return (
        <div className="space-y-6">
            <section className="grid gap-4 xl:grid-cols-[1.2fr_0.8fr_0.8fr]">
                <article className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                    <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800">
                        <ShieldCheck className="h-4 w-4" />
                        Manual vendor settlement review
                    </div>
                    <h1 className="mt-5 text-3xl font-semibold tracking-tight text-slate-950">Proof-backed debt repayment queue</h1>
                    <p className="mt-4 text-sm leading-7 text-slate-600">
                        Approvals create paired CommerceHub settlement entries only after proof review. Rejections stay outside the ledger and remain fully auditable.
                    </p>
                </article>

                <article className="rounded-[32px] border border-amber-200 bg-amber-50 p-6 shadow-sm">
                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-amber-700">Pending Review</p>
                    <p className="mt-3 text-3xl font-semibold text-amber-950">{pendingCount}</p>
                    <p className="mt-2 text-sm text-amber-900/80">Requests waiting for manual verification.</p>
                </article>

                <article className="rounded-[32px] border border-emerald-200 bg-emerald-50 p-6 shadow-sm">
                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-emerald-700">Approved In View</p>
                    <p className="mt-3 text-3xl font-semibold text-emerald-950">{approvedCount}</p>
                    <p className="mt-2 text-sm text-emerald-900/80">Approved row count only. Ledger money remains backend-owned and visible per approved request.</p>
                </article>
            </section>

            <section className="grid gap-6 xl:grid-cols-[0.95fr_1.05fr]">
                <article className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                    <div className="flex items-center gap-3">
                        <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-700">
                            <CreditCard className="h-5 w-5" />
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Payment Methods</p>
                            <h2 className="mt-1 text-2xl font-semibold text-slate-950">Platform destinations</h2>
                        </div>
                    </div>

                    <div className="mt-6 space-y-3">
                        {methodCards.map((method: SuperAdminVendorPaymentMethod) => (
                            <div key={method.id} className="rounded-[24px] border border-slate-200 bg-slate-50 px-4 py-4">
                                <div className="flex items-start justify-between gap-3">
                                    <div>
                                        <p className="font-semibold text-slate-950">{method.name}</p>
                                        <p className="mt-1 text-sm text-slate-600">{method.destinationValue}</p>
                                        <p className="mt-2 text-sm leading-6 text-slate-500">{method.instructions}</p>
                                    </div>
                                    <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold ${method.isActive ? 'border-emerald-200 bg-white text-emerald-700' : 'border-slate-200 bg-white text-slate-500'}`}>
                                        {method.isActive ? 'Active' : 'Inactive'}
                                    </span>
                                </div>
                            </div>
                        ))}
                    </div>

                    <div className="mt-6 space-y-3 border-t border-slate-100 pt-6">
                        <input value={methodForm.name} onChange={(event) => setMethodForm((current) => ({ ...current, name: event.target.value }))} placeholder="Method name" className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100" />
                        <input value={methodForm.destinationValue} onChange={(event) => setMethodForm((current) => ({ ...current, destinationValue: event.target.value }))} placeholder="Destination value" className="w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100" />
                        <textarea value={methodForm.instructions} onChange={(event) => setMethodForm((current) => ({ ...current, instructions: event.target.value }))} placeholder="Instructions shown to vendors" rows={4} className="w-full rounded-[24px] border border-slate-200 px-4 py-3 text-sm outline-none focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100" />
                        <button type="button" onClick={() => createMethodMutation.mutate()} disabled={createMethodMutation.isPending} className="inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60">
                            {createMethodMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Upload className="h-4 w-4" />}
                            Add payment method
                        </button>
                    </div>
                </article>

                <article className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                    <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                        <select value={filters.status ?? ''} onChange={(event) => updateFilters({ status: event.target.value as SuperAdminVendorPaymentFilters['status'] })} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100">
                            <option value="">All statuses</option>
                            {SUPER_ADMIN_VENDOR_PAYMENT_STATUSES.map((status) => <option key={status} value={status}>{status}</option>)}
                        </select>
                        <select value={filters.vendorId ?? ''} onChange={(event) => updateFilters({ vendorId: event.target.value })} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100">
                            <option value="">{tenantsQuery.isLoading ? 'Loading vendors...' : 'All vendors'}</option>
                            {(tenantsQuery.data || []).map((tenant) => <option key={tenant.id} value={tenant.id}>{tenant.name}</option>)}
                        </select>
                        <div className="relative md:col-span-2">
                            <Search className="pointer-events-none absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                            <input value={filters.search ?? ''} onChange={(event) => updateFilters({ search: event.target.value })} placeholder="Search vendor, method, or transfer reference" className="w-full rounded-2xl border border-slate-200 py-3 pl-11 pr-4 text-sm outline-none focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100" />
                        </div>
                    </div>

                    <div className="mt-6 overflow-x-auto">
                        <table className="min-w-full divide-y divide-slate-100">
                            <thead className="bg-slate-50/90">
                                <tr className="text-left text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">
                                    <th className="px-4 py-4">Vendor</th><th className="px-4 py-4">Amount</th><th className="px-4 py-4">Method</th><th className="px-4 py-4">Status</th><th className="px-4 py-4">Created</th><th className="px-4 py-4 text-right">Actions</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-slate-100">
                                {requests.map((request) => (
                                    <tr key={request.paymentRequestId}>
                                        <td className="px-4 py-4"><p className="font-semibold text-slate-950">{request.vendorName}</p><p className="mt-1 text-sm text-slate-500">{request.transferReference || 'No transfer reference'}</p></td>
                                        <td className="px-4 py-4 font-semibold text-slate-950">{formatCurrency(request.amount, request.currency)}</td>
                                        <td className="px-4 py-4 text-sm text-slate-600"><p className="font-medium text-slate-900">{request.methodName}</p><p className="mt-1">{request.destinationValue}</p></td>
                                        <td className="px-4 py-4"><span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold ${statusTone(request.status)}`}>{request.status}</span></td>
                                        <td className="px-4 py-4 text-sm text-slate-600"><p>{formatDateTime(request.createdAtUtc)}</p><p className="mt-1 text-xs text-slate-500">{request.reviewReason || 'Awaiting review'}</p></td>
                                        <td className="px-4 py-4">
                                            <div className="flex justify-end gap-2">
                                                <button type="button" onClick={() => handleOpenProof(request.paymentRequestId)} className="rounded-2xl border border-slate-200 px-3 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50">Proof</button>
                                                {request.status === 'PendingVerification' ? (
                                                    <>
                                                        <button type="button" onClick={() => openActionModal('approve', request)} className="rounded-2xl bg-emerald-600 px-3 py-2 text-sm font-semibold text-white transition hover:bg-emerald-700">Approve</button>
                                                        <button type="button" onClick={() => openActionModal('reject', request)} className="rounded-2xl bg-rose-600 px-3 py-2 text-sm font-semibold text-white transition hover:bg-rose-700">Reject</button>
                                                    </>
                                                ) : null}
                                            </div>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>

                    <div className="mt-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                        <label className="flex items-center gap-3 text-sm text-slate-600">
                            <span>Page size</span>
                            <select value={filters.pageSize} onChange={(event) => updateFilters({ pageSize: Number(event.target.value) })} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100">
                                {PAGE_SIZE_OPTIONS.map((size) => <option key={size} value={size}>{size}</option>)}
                            </select>
                        </label>
                        <div className="flex items-center gap-3">
                            <button type="button" disabled={filters.page <= 1} onClick={() => updateFilters({ page: filters.page - 1 })} className="rounded-2xl border border-slate-200 px-5 py-3 text-sm font-semibold text-slate-700 disabled:cursor-not-allowed disabled:opacity-50">Previous</button>
                            <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm font-medium text-slate-700">Page {filters.page} of {totalPages}</div>
                            <button type="button" disabled={filters.page >= totalPages} onClick={() => updateFilters({ page: filters.page + 1 })} className="rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50">Next</button>
                        </div>
                    </div>
                </article>
            </section>

            {actionModal ? (
                <div className="fixed inset-0 z-[70] flex items-center justify-center bg-slate-950/55 px-4">
                    <div className="w-full max-w-xl rounded-[32px] border border-slate-200 bg-white p-6 shadow-2xl">
                        <div className="flex items-start gap-3">
                            <div className={`flex h-12 w-12 items-center justify-center rounded-2xl ${actionModal.mode === 'approve' ? 'bg-emerald-50 text-emerald-700' : 'bg-rose-50 text-rose-700'}`}>
                                {actionModal.mode === 'approve' ? <CheckCircle2 className="h-5 w-5" /> : <AlertTriangle className="h-5 w-5" />}
                            </div>
                            <div>
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{actionModal.mode === 'approve' ? 'Approve' : 'Reject'} Payment</p>
                                <h2 className="mt-1 text-2xl font-semibold text-slate-950">{currentActionRequest?.vendorName}</h2>
                                <p className="mt-2 text-sm leading-6 text-slate-600">{currentActionRequest ? `${formatCurrency(currentActionRequest.amount, currentActionRequest.currency)} via ${currentActionRequest.methodName}` : 'Refreshing current payment authority...'}</p>
                            </div>
                        </div>
                        <textarea value={actionReason} onChange={(event) => setActionReason(event.target.value)} rows={4} placeholder={actionModal.mode === 'approve' ? 'Why is this proof valid and ready for ledger posting?' : 'Why is this payment request being rejected?'} className="mt-5 w-full rounded-[24px] border border-slate-200 px-4 py-3 text-sm outline-none focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100" />
                        {actionModal.mode === 'approve' ? (
                            <label className="mt-4 flex items-start gap-3 rounded-[24px] border border-amber-200 bg-amber-50 px-4 py-4 text-sm text-amber-950">
                                <input type="checkbox" checked={allowOverpayment} onChange={(event) => setAllowOverpayment(event.target.checked)} className="mt-1 h-4 w-4 rounded border-amber-300" />
                                <span>Allow approval even if the submitted amount now exceeds current vendor debt.</span>
                            </label>
                        ) : null}
                        <div className="mt-6 flex justify-end gap-3">
                            <button type="button" onClick={closeActionModal} disabled={isActionSubmitting} className="rounded-2xl border border-slate-200 px-5 py-3 text-sm font-semibold text-slate-700">Cancel</button>
                            <button type="button" onClick={handleActionConfirm} disabled={isActionSubmitting || !currentActionRequest} className={`inline-flex items-center gap-2 rounded-2xl px-5 py-3 text-sm font-semibold text-white ${actionModal.mode === 'approve' ? 'bg-emerald-600 hover:bg-emerald-700' : 'bg-rose-600 hover:bg-rose-700'}`}>
                                {isActionSubmitting ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
                                {actionModal.mode === 'approve' ? 'Approve request' : 'Reject request'}
                            </button>
                        </div>
                    </div>
                </div>
            ) : null}
        </div>
    )
}

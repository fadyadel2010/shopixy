import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { ArrowRight, PackageCheck, RefreshCw, ShieldCheck, Store, X } from 'lucide-react'
import { useAuthStore } from '../store/authStore'
import {
    confirmCommerceHubVendorReturnReceipt,
    getCommerceHubVendorReturnActions,
    getCommerceHubVendorOrder,
    getCommerceHubVendorOrderActions,
    getCommerceHubVendorOrders,
    runCommerceHubVendorOrderAction,
    runCommerceHubVendorReturnAction,
} from '../services/commerceHubVendorOrdersApi'

const PAGE_SIZE = 20

const money = (amount, currency = 'EGP') => new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
}).format(Number(amount || 0))

const dateTime = (value) => value ? new Date(value).toLocaleString() : 'Unavailable'
const dateTimeInputNow = () => {
    const now = new Date()
    return new Date(now.getTime() - now.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}

function Pill({ children, tone = 'slate' }) {
    const colors = {
        slate: 'border-slate-200 bg-slate-100 text-slate-700',
        blue: 'border-blue-200 bg-blue-50 text-blue-700',
        green: 'border-emerald-200 bg-emerald-50 text-emerald-700',
        amber: 'border-amber-200 bg-amber-50 text-amber-800',
        red: 'border-rose-200 bg-rose-50 text-rose-700',
    }
    return <span className={`inline-flex rounded-full border px-2.5 py-1 text-xs font-semibold ${colors[tone]}`}>{children}</span>
}

const statusTone = (status) => ['Delivered', 'Completed', 'Collected', 'Settled', 'Paid', 'Confirmed'].includes(status)
    ? 'green'
    : ['Cancelled', 'Refunded', 'Failed', 'Rejected'].includes(status)
        ? 'red'
        : ['Pending', 'CashOnDelivery', 'WaitingForReturn'].includes(status)
            ? 'amber'
            : 'blue'

function Fact({ label, children }) {
    return <div><dt className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">{label}</dt><dd className="mt-1.5 text-sm font-medium text-slate-900">{children || 'Unavailable'}</dd></div>
}

function ReturnActions({ returnRequest, onAction, pending }) {
    const [selectedAction, setSelectedAction] = useState(null)
    const [trackingNumber, setTrackingNumber] = useState(returnRequest.trackingNumber || '')
    const [courierName, setCourierName] = useState(returnRequest.courierName || '')
    const [notes, setNotes] = useState('')
    const [timestamp, setTimestamp] = useState(dateTimeInputNow)
    const actions = getCommerceHubVendorReturnActions(returnRequest)

    useEffect(() => {
        setSelectedAction(null)
        setTrackingNumber(returnRequest.trackingNumber || '')
        setCourierName(returnRequest.courierName || '')
        setNotes('')
        setTimestamp(dateTimeInputNow())
    }, [returnRequest.returnId, returnRequest.status, returnRequest.trackingNumber, returnRequest.courierName])

    if (!actions.length) {
        return null
    }

    return <div className="mt-4 rounded-2xl border border-blue-200 bg-white/80 p-4">
        <p className="text-xs font-semibold uppercase tracking-[0.16em] text-blue-700">Vendor return logistics</p>
        <div className="mt-3 flex flex-wrap gap-2">{actions.map((action) => <button key={action.code} type="button" disabled={pending} onClick={() => { setSelectedAction(action); setNotes('') }} className={action.isFailure ? 'rounded-full border border-rose-200 bg-white px-4 py-2 text-sm font-semibold text-rose-700' : 'rounded-full bg-blue-700 px-4 py-2 text-sm font-semibold text-white'}>{action.label}</button>)}</div>
        {selectedAction ? <form className="mt-4 space-y-3" onSubmit={(event) => { event.preventDefault(); onAction(returnRequest.returnId, selectedAction.code, { timestampUtc: timestamp ? new Date(timestamp).toISOString() : null, trackingNumber, courierName, notes }) }}>
            <div className="grid gap-3 sm:grid-cols-3"><label className="text-sm text-slate-700">Event time<input required type="datetime-local" value={timestamp} onChange={(event) => setTimestamp(event.target.value)} className="mt-1 w-full rounded-xl border border-slate-200 bg-white p-2" /></label><label className="text-sm text-slate-700">Tracking number<input maxLength={200} value={trackingNumber} onChange={(event) => setTrackingNumber(event.target.value)} className="mt-1 w-full rounded-xl border border-slate-200 bg-white p-2" /></label><label className="text-sm text-slate-700">Courier<input maxLength={200} value={courierName} onChange={(event) => setCourierName(event.target.value)} className="mt-1 w-full rounded-xl border border-slate-200 bg-white p-2" /></label></div>
            <label className="block text-sm text-slate-700">{selectedAction.isFailure ? 'Failure reason' : 'Logistics notes'}<textarea required={selectedAction.isFailure} maxLength={1000} value={notes} onChange={(event) => setNotes(event.target.value)} className="mt-1 min-h-20 w-full rounded-xl border border-slate-200 bg-white p-2" /></label>
            <div className="flex gap-2"><button disabled={pending || (selectedAction.isFailure && !notes.trim())} className={selectedAction.isFailure ? 'rounded-full bg-rose-700 px-4 py-2 text-sm font-semibold text-white disabled:opacity-40' : 'rounded-full bg-slate-950 px-4 py-2 text-sm font-semibold text-white disabled:opacity-40'}>{pending ? 'Applying' : `Confirm ${selectedAction.label.toLowerCase()}`}</button><button type="button" onClick={() => setSelectedAction(null)} className="rounded-full border border-slate-300 bg-white px-4 py-2 text-sm font-semibold">Back</button></div>
        </form> : null}
    </div>
}

function OrderDetail({ order, actions, actionsLoading, onClose, onAction, actionPending, onReturnAction, returnActionPending, returnActionError, onReceipt, receiptPending }) {
    const [selectedAction, setSelectedAction] = useState(null)
    const [reason, setReason] = useState('')
    const [receipt, setReceipt] = useState(null)
    const lineage = order.marketplaceLineage

    useEffect(() => {
        setSelectedAction(null)
        setReason('')
        setReceipt(null)
    }, [order.orderId, order.status])

    return <aside className="fixed inset-y-0 end-0 z-50 w-full max-w-3xl overflow-y-auto border-s border-slate-200 bg-slate-50 shadow-2xl" aria-label="CommerceHub vendor order detail">
        <header className="sticky top-0 z-10 border-b border-slate-200 bg-white/95 px-5 py-5 backdrop-blur sm:px-7">
            <div className="flex items-start justify-between gap-4">
                <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-blue-700">Your CommerceHub order</p>
                    <h2 className="mt-2 text-2xl font-semibold text-slate-950">{order.orderNumber}</h2>
                    <p className="mt-1 text-sm text-slate-500">Vendor sub-order within marketplace order {lineage.marketplaceOrderNumber}</p>
                </div>
                <button type="button" onClick={onClose} className="rounded-full border border-slate-200 bg-white p-2 text-slate-600" aria-label="Close detail"><X className="h-4 w-4" /></button>
            </div>
        </header>

        <div className="space-y-5 p-5 sm:p-7">
            <section className="overflow-hidden rounded-3xl bg-slate-950 p-5 text-white">
                <div className="flex flex-wrap items-center justify-between gap-3">
                    <div><p className="text-xs uppercase tracking-[0.18em] text-blue-200">Vendor sub-order</p><p className="mt-2 text-xl font-semibold">{lineage.marketplaceSubOrderNumber}</p></div>
                    <Pill tone="blue">CommerceHub Marketplace</Pill>
                </div>
                <dl className="mt-5 grid gap-4 sm:grid-cols-3 [&_dt]:text-blue-200 [&_dd]:text-white">
                    <Fact label="Parent order">{lineage.marketplaceOrderNumber}</Fact>
                    <Fact label="Vendor">{lineage.vendorName}</Fact>
                    <Fact label="Store ID">{lineage.vendorId}</Fact>
                </dl>
            </section>

            <section className="rounded-3xl border border-slate-200 bg-white p-5">
                <h3 className="text-sm font-semibold uppercase tracking-[0.18em] text-slate-500">Lifecycle authority</h3>
                <div className="mt-4 flex flex-wrap gap-2"><Pill tone={statusTone(order.status)}>{order.status}</Pill><Pill tone={statusTone(order.paymentStatus)}>{order.paymentStatus}</Pill></div>
                <dl className="mt-5 grid gap-4 sm:grid-cols-3">
                    <Fact label="Fulfillment status">{actions?.currentStatus || order.status}</Fact>
                    <Fact label="Payment readiness">{actionsLoading ? 'Checking' : actions?.paymentReady ? 'Ready' : 'Blocked'}</Fact>
                    <Fact label="Inventory readiness">{actionsLoading ? 'Checking' : actions?.inventoryReady ? 'Ready' : 'Blocked'}</Fact>
                </dl>
                {actions?.paymentReadinessMessage ? <p className="mt-4 rounded-2xl bg-amber-50 p-3 text-sm text-amber-800">{actions.paymentReadinessMessage}</p> : null}
                {actions?.inventoryReadinessMessage ? <p className="mt-3 rounded-2xl bg-amber-50 p-3 text-sm text-amber-800">{actions.inventoryReadinessMessage}</p> : null}
                {actions?.lifecycleMessage ? <p className="mt-3 text-sm text-slate-600">{actions.lifecycleMessage}</p> : null}
                <div className="mt-5 flex flex-wrap gap-2">
                    {(actions?.availableActions || []).map((action) => <button key={action.code} type="button" disabled={actionPending} onClick={() => { setSelectedAction(action); setReason('') }} className={action.code === 'cancel' ? 'rounded-full border border-rose-200 px-4 py-2 text-sm font-semibold text-rose-700' : 'rounded-full bg-blue-700 px-4 py-2 text-sm font-semibold text-white'}>{action.label}</button>)}
                    {!actionsLoading && !actions?.availableActions?.length ? <p className="text-sm text-slate-500">No backend-authorized fulfillment action is currently available.</p> : null}
                </div>
                {selectedAction ? <form className="mt-4 rounded-2xl border border-blue-200 bg-blue-50 p-4" onSubmit={(event) => { event.preventDefault(); onAction(selectedAction.code, reason) }}>
                    <p className="font-semibold text-slate-950">Confirm {selectedAction.label}</p>
                    {selectedAction.requiresReason ? <textarea required maxLength={500} value={reason} onChange={(event) => setReason(event.target.value)} placeholder="Cancellation reason" className="mt-3 min-h-24 w-full rounded-2xl border border-slate-200 bg-white p-3 text-sm" /> : <p className="mt-2 text-sm text-slate-600">The backend will re-read current lifecycle, payment, inventory, vendor, and version authority before applying this action.</p>}
                    <div className="mt-3 flex gap-2"><button type="submit" disabled={actionPending || (selectedAction.requiresReason && !reason.trim())} className="rounded-full bg-slate-950 px-4 py-2 text-sm font-semibold text-white disabled:opacity-40">{actionPending ? 'Applying' : 'Confirm action'}</button><button type="button" onClick={() => setSelectedAction(null)} className="rounded-full border border-slate-300 bg-white px-4 py-2 text-sm font-semibold">Back</button></div>
                </form> : null}
            </section>

            <section className="rounded-3xl border border-slate-200 bg-white p-5">
                <h3 className="text-sm font-semibold uppercase tracking-[0.18em] text-slate-500">Customer and delivery</h3>
                <dl className="mt-4 grid gap-4 sm:grid-cols-2"><Fact label="Customer">{order.customer?.name}</Fact><Fact label="Phone">{order.customer?.phone}</Fact><Fact label="Address">{[order.shippingAddress?.address, order.shippingAddress?.city, order.shippingAddress?.state].filter(Boolean).join(', ')}</Fact><Fact label="Shipping method">{lineage.shippingMethodName || 'Marketplace shipping snapshot'}</Fact></dl>
            </section>

            <section className="rounded-3xl border border-slate-200 bg-white p-5">
                <h3 className="text-sm font-semibold uppercase tracking-[0.18em] text-slate-500">Items and returns</h3>
                {returnActionError ? <p role="alert" className="mt-4 rounded-2xl border border-rose-200 bg-rose-50 p-3 text-sm text-rose-800">{returnActionError.message}</p> : null}
                <div className="mt-4 space-y-4">{order.items.map((item) => <article key={item.itemId} className="rounded-2xl border border-slate-200 p-4">
                    <div className="flex items-start justify-between gap-4"><div><p className="font-semibold text-slate-950">{item.productName}</p><p className="mt-1 text-sm text-slate-500">{item.variantName || 'Standard variant'} | Quantity {item.quantity}</p></div><p className="font-semibold">{money(item.total, order.totals.currency)}</p></div>
                    {(item.refunds || []).map((refund) => <div key={refund.refundId} className="mt-4 rounded-2xl border border-amber-200 bg-amber-50 p-4">
                        <div className="flex flex-wrap justify-between gap-3"><div><p className="text-xs font-semibold uppercase tracking-[0.16em] text-amber-800">Refund {refund.refundId}</p><p className="mt-2 text-sm text-slate-700">{refund.reason}</p></div><div className="text-right"><Pill tone={statusTone(refund.status)}>{refund.status}</Pill><p className="mt-2 font-semibold">{money(refund.amount, order.totals.currency)} | Qty {refund.quantity}</p></div></div>
                        <dl className="mt-4 grid gap-3 sm:grid-cols-3"><Fact label="Return state">{refund.returnRequest?.status || 'Not created'}</Fact><Fact label="Next action">{refund.nextAction}</Fact><Fact label="Execution state">{refund.executionState}</Fact></dl>
                        {refund.returnRequest ? <ReturnActions returnRequest={refund.returnRequest} onAction={onReturnAction} pending={returnActionPending} /> : null}
                        {refund.returnRequest?.canConfirmReceipt ? <button type="button" onClick={() => setReceipt({ refund, resellable: String(refund.returnRequest.requestedQuantity), damaged: '0', notes: '' })} className="mt-4 rounded-full bg-slate-950 px-4 py-2 text-sm font-semibold text-white">Confirm vendor receipt</button> : refund.returnRequest && refund.returnRequest.status !== 'Confirmed' ? <p className="mt-4 text-sm text-slate-600">Vendor receipt is unavailable until backend logistics reaches DeliveredToVendor.</p> : null}
                        {receipt?.refund.refundId === refund.refundId ? <form className="mt-4 rounded-2xl border border-slate-200 bg-white p-4" onSubmit={(event) => { event.preventDefault(); onReceipt(refund.returnRequest.returnId, receipt) }}><div className="grid gap-3 sm:grid-cols-2"><label className="text-sm">Resellable<input type="number" min="0" step="1" value={receipt.resellable} onChange={(event) => setReceipt({ ...receipt, resellable: event.target.value })} className="mt-1 w-full rounded-xl border p-2" /></label><label className="text-sm">Damaged<input type="number" min="0" step="1" value={receipt.damaged} onChange={(event) => setReceipt({ ...receipt, damaged: event.target.value })} className="mt-1 w-full rounded-xl border p-2" /></label></div><textarea value={receipt.notes} onChange={(event) => setReceipt({ ...receipt, notes: event.target.value })} placeholder="Receipt notes (optional)" className="mt-3 w-full rounded-xl border p-2 text-sm" /><button disabled={receiptPending} className="mt-3 rounded-full bg-blue-700 px-4 py-2 text-sm font-semibold text-white">{receiptPending ? 'Confirming' : 'Confirm receipt only'}</button></form> : null}
                    </div>)}
                </article>)}</div>
            </section>

            <section className="rounded-3xl border border-slate-200 bg-white p-5">
                <h3 className="text-sm font-semibold uppercase tracking-[0.18em] text-slate-500">Backend totals</h3>
                <dl className="mt-4 grid gap-4 sm:grid-cols-3"><Fact label="Subtotal">{money(order.totals.subtotal, order.totals.currency)}</Fact><Fact label="Shipping">{money(order.totals.shippingCost, order.totals.currency)}</Fact><Fact label="Total">{money(order.totals.total, order.totals.currency)}</Fact></dl>
            </section>

            <section className="rounded-3xl border border-slate-200 bg-white p-5">
                <h3 className="text-sm font-semibold uppercase tracking-[0.18em] text-slate-500">Lifecycle history</h3>
                <div className="mt-4 space-y-3">{order.commerceHubLifecycleHistory?.length ? order.commerceHubLifecycleHistory.map((entry) => <div key={entry.historyId} className="border-s-2 border-blue-200 ps-4"><p className="text-sm font-semibold">{entry.previousStatus} to {entry.newStatus}</p><p className="mt-1 text-xs text-slate-500">{dateTime(entry.changedAtUtc)}{entry.reason ? ` | ${entry.reason}` : ''}</p></div>) : <p className="text-sm text-slate-500">No lifecycle history was returned.</p>}</div>
            </section>

            <nav className="flex flex-wrap gap-3 text-sm font-semibold text-blue-700"><Link to="/commercehub/finance">Finance</Link><Link to="/commercehub/earnings">Earnings</Link><Link to="/commercehub/payments">Payments</Link></nav>
        </div>
    </aside>
}

export default function CommerceHubOrders() {
    const user = useAuthStore((state) => state.user)
    const { subOrderId } = useParams()
    const navigate = useNavigate()
    const location = useLocation()
    const queryClient = useQueryClient()
    const [page, setPage] = useState(1)
    const [search, setSearch] = useState('')
    const scope = ['commercehub-vendor-orders', user?.tenantId, user?.id]
    const filters = { page, pageSize: PAGE_SIZE, search, sortBy: 'createdAt', sortDirection: 'desc' }
    const listQuery = useQuery({ queryKey: [...scope, 'list', filters], queryFn: ({ signal }) => getCommerceHubVendorOrders(filters, signal), staleTime: 20_000 })
    const detailQuery = useQuery({ queryKey: [...scope, 'detail', subOrderId], queryFn: ({ signal }) => getCommerceHubVendorOrder(subOrderId, signal), enabled: Boolean(subOrderId), staleTime: 15_000 })
    const actionsQuery = useQuery({ queryKey: [...scope, 'actions', subOrderId], queryFn: ({ signal }) => getCommerceHubVendorOrderActions(subOrderId, signal), enabled: Boolean(subOrderId && detailQuery.data), staleTime: 10_000 })

    const refresh = async () => Promise.all([
        queryClient.invalidateQueries({ queryKey: [...scope, 'list'] }),
        queryClient.invalidateQueries({ queryKey: [...scope, 'detail', subOrderId] }),
        queryClient.invalidateQueries({ queryKey: [...scope, 'actions', subOrderId] }),
    ])
    const actionMutation = useMutation({ mutationFn: ({ code, reason }) => runCommerceHubVendorOrderAction(subOrderId, code, reason), onSuccess: refresh, onError: (error) => { if (error?.status === 409) void refresh() } })
    const returnActionMutation = useMutation({ mutationFn: ({ returnId, code, payload }) => runCommerceHubVendorReturnAction(subOrderId, returnId, code, payload), onSuccess: refresh, onError: (error) => { if ([409, 428].includes(error?.status)) void refresh() } })
    const receiptMutation = useMutation({ mutationFn: ({ returnId, receipt }) => confirmCommerceHubVendorReturnReceipt(returnId, { resellableQuantity: Number(receipt.resellable), damagedQuantity: Number(receipt.damaged), notes: receipt.notes.trim() || null }), onSuccess: refresh, onError: (error) => { if (error?.status === 409) void refresh() } })
    const orders = listQuery.data?.items || []

    return <div className="space-y-6">
        <section className="relative overflow-hidden rounded-[32px] bg-slate-950 px-6 py-8 text-white shadow-xl sm:px-9">
            <div className="absolute -end-24 -top-24 h-64 w-64 rounded-full bg-blue-600/30 blur-3xl" />
            <div className="relative flex flex-col gap-6 lg:flex-row lg:items-end lg:justify-between">
                <div><div className="inline-flex items-center gap-2 rounded-full border border-blue-400/40 bg-blue-400/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.2em] text-blue-200"><Store className="h-3.5 w-3.5" />CommerceHub Marketplace Orders</div><h1 className="mt-5 text-3xl font-semibold tracking-tight sm:text-4xl">Vendor orders, isolated.</h1><p className="mt-3 max-w-2xl text-sm leading-6 text-slate-300">Only CommerceHub sub-orders owned by your authenticated vendor store appear here. Fulfillment commands remain SQL-authoritative.</p></div>
                <button type="button" onClick={() => listQuery.refetch()} disabled={listQuery.isFetching} className="inline-flex items-center justify-center gap-2 rounded-full border border-white/20 px-4 py-2 text-sm font-semibold"><RefreshCw className={`h-4 w-4 ${listQuery.isFetching ? 'animate-spin' : ''}`} />Refresh authority</button>
            </div>
        </section>

        <section className="rounded-3xl border border-slate-200 bg-white p-5 shadow-sm"><label className="text-sm font-semibold text-slate-700">Search CommerceHub order, customer, or phone<input type="search" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1) }} className="mt-2 w-full rounded-2xl border border-slate-200 px-4 py-3 outline-none focus:border-blue-400" /></label></section>

        {listQuery.isError ? <section role="alert" className="rounded-3xl border border-rose-200 bg-rose-50 p-6 text-rose-800"><p className="font-semibold">CommerceHub orders are unavailable.</p><p className="mt-2 text-sm">{listQuery.error?.message}</p></section> : null}

        {!listQuery.isError ? <section className="overflow-hidden rounded-3xl border border-slate-200 bg-white shadow-sm">
            <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4"><div><p className="font-semibold text-slate-950">{listQuery.data?.totalCount || 0} vendor orders</p><p className="mt-1 text-xs text-slate-500">Marketplace sub-orders only</p></div><ShieldCheck className="h-5 w-5 text-emerald-600" /></div>
            <div className="grid gap-3 p-4 sm:p-5">{listQuery.isLoading ? <p className="py-10 text-center text-sm text-slate-500">Loading SQL order authority...</p> : orders.map((order) => {
                const lineage = order.marketplaceLineage
                return <button key={order.orderId} type="button" onClick={() => navigate(`/commercehub/orders/${order.orderId}${location.search}`)} className="group grid gap-4 rounded-2xl border border-slate-200 p-4 text-start transition hover:border-blue-300 hover:bg-blue-50/40 md:grid-cols-[1.3fr_1fr_1fr_auto] md:items-center">
                    <div><p className="text-xs font-semibold uppercase tracking-[0.16em] text-blue-700">{order.orderNumber}</p><p className="mt-1 text-sm font-semibold text-slate-950">Parent {lineage.marketplaceOrderNumber}</p><p className="mt-1 text-xs text-slate-500">{order.customerName || 'Marketplace customer'}</p></div>
                    <div><p className="text-sm font-medium text-slate-900">{order.productSummary || `${order.itemCount} items`}</p><p className="mt-1 text-xs text-slate-500">{order.itemCount} line(s) | Qty {order.totalQuantity}</p></div>
                    <div className="flex flex-wrap gap-2"><Pill tone={statusTone(order.status)}>{order.status}</Pill><Pill tone={statusTone(order.paymentStatus)}>{order.paymentStatus}</Pill>{order.returnStatus ? <Pill tone="amber">Return {order.returnStatus}</Pill> : null}</div>
                    <div className="flex items-center justify-between gap-4 md:justify-end"><div className="text-end"><p className="font-semibold text-slate-950">{money(order.totalAmount, order.currency)}</p><p className="mt-1 text-xs text-slate-500">{dateTime(order.createdAtUtc)}</p></div><ArrowRight className="h-4 w-4 text-blue-600 transition group-hover:translate-x-1" /></div>
                </button>
            })}{!listQuery.isLoading && !orders.length ? <div className="py-14 text-center"><PackageCheck className="mx-auto h-8 w-8 text-slate-300" /><p className="mt-3 text-sm text-slate-500">No CommerceHub vendor orders matched this scope.</p></div> : null}</div>
            <div className="flex items-center justify-between border-t border-slate-200 px-5 py-4"><p className="text-sm text-slate-500">Page {listQuery.data?.page || page} of {Math.max(1, listQuery.data?.totalPages || 1)}</p><div className="flex gap-2"><button disabled={page <= 1 || listQuery.isFetching} onClick={() => setPage((value) => value - 1)} className="rounded-full border px-4 py-2 text-sm font-semibold disabled:opacity-40">Previous</button><button disabled={!listQuery.data?.hasNextPage || listQuery.isFetching} onClick={() => setPage((value) => value + 1)} className="rounded-full border px-4 py-2 text-sm font-semibold disabled:opacity-40">Next</button></div></div>
        </section> : null}

        {subOrderId ? <><div className="fixed inset-0 z-40 bg-slate-950/40" onClick={() => navigate('/commercehub/orders')} />{detailQuery.data ? <OrderDetail order={detailQuery.data} actions={actionsQuery.data} actionsLoading={actionsQuery.isLoading} onClose={() => navigate('/commercehub/orders')} onAction={(code, reason) => actionMutation.mutate({ code, reason })} actionPending={actionMutation.isPending} onReturnAction={(returnId, code, payload) => returnActionMutation.mutate({ returnId, code, payload })} returnActionPending={returnActionMutation.isPending} returnActionError={returnActionMutation.error} onReceipt={(returnId, receipt) => receiptMutation.mutate({ returnId, receipt })} receiptPending={receiptMutation.isPending} /> : <aside className="fixed inset-y-0 end-0 z-50 w-full max-w-3xl bg-white p-8 shadow-2xl"><p>{detailQuery.isError ? detailQuery.error?.message : 'Loading CommerceHub order...'}</p></aside>}</> : null}
    </div>
}

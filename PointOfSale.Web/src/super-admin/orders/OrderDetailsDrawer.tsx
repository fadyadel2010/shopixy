import { useEffect, useMemo, useState } from 'react'
import {
    AlertTriangle,
    Bug,
    ChevronDown,
    ChevronUp,
    CircleDollarSign,
    Clock3,
    CreditCard,
    Loader2,
    Package,
    ReceiptText,
    Store,
    UserRound,
    X,
} from 'lucide-react'
import {
    isFailedPaymentStatus,
    isPendingOrderStatus,
    type SuperAdminOrderDetails,
    type SuperAdminOrderItem,
    type SuperAdminOrderSubOrderDetails,
} from './ordersService'

interface OrderDetailsDrawerProps {
    isOpen: boolean
    order?: SuperAdminOrderDetails | null
    isLoading: boolean
    isError: boolean
    errorMessage?: string | null
    onClose: () => void
    onRetry: () => void
}

const STUCK_THRESHOLD_MINUTES = 120

function formatCurrency(amount: number, currency: string): string {
    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: currency || 'EGP',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(amount)
}

function formatPercent(rate: number): string {
    return `${new Intl.NumberFormat('en-US', {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2,
    }).format(rate * 100)}%`
}

function formatDateTime(value?: string | null): string {
    if (!value) {
        return 'Unavailable'
    }

    const parsed = new Date(value)
    if (Number.isNaN(parsed.getTime())) {
        return 'Unavailable'
    }

    return parsed.toLocaleString()
}

function getRelativeAge(value?: string | null): { label: string; isStuck: boolean } {
    if (!value) {
        return {
            label: 'Awaiting sync',
            isStuck: false,
        }
    }

    const parsed = new Date(value)
    if (Number.isNaN(parsed.getTime())) {
        return {
            label: 'Awaiting sync',
            isStuck: false,
        }
    }

    const diffMs = Math.max(0, Date.now() - parsed.getTime())
    const minutesOld = Math.floor(diffMs / 60_000)

    if (minutesOld < 1) {
        return { label: 'Just created', isStuck: false }
    }

    if (minutesOld < 60) {
        return {
            label: `${minutesOld} minute${minutesOld === 1 ? '' : 's'} old`,
            isStuck: minutesOld >= STUCK_THRESHOLD_MINUTES,
        }
    }

    const hoursOld = Math.floor(minutesOld / 60)
    if (hoursOld < 24) {
        return {
            label: `${hoursOld} hour${hoursOld === 1 ? '' : 's'} old`,
            isStuck: minutesOld >= STUCK_THRESHOLD_MINUTES,
        }
    }

    const daysOld = Math.floor(hoursOld / 24)
    return {
        label: `${daysOld} day${daysOld === 1 ? '' : 's'} old`,
        isStuck: minutesOld >= STUCK_THRESHOLD_MINUTES,
    }
}

function formatJson(value?: string | null): string {
    if (!value?.trim()) {
        return 'Unavailable'
    }

    try {
        return JSON.stringify(JSON.parse(value), null, 2)
    } catch {
        return value
    }
}

function DetailCard({
    label,
    value,
    tone = 'border-slate-200 bg-white text-slate-950',
}: {
    label: string
    value: string
    tone?: string
}) {
    return (
        <div className={`rounded-[24px] border px-4 py-4 shadow-sm ${tone}`}>
            <p className="text-xs font-semibold uppercase tracking-[0.18em] opacity-70">{label}</p>
            <p className="mt-3 text-lg font-semibold">{value}</p>
        </div>
    )
}

function DrawerSkeleton() {
    return (
        <div className="space-y-5 animate-pulse">
            <div className="grid gap-4 sm:grid-cols-2">
                {Array.from({ length: 4 }).map((_, index) => (
                    <div key={`drawer-card-${index}`} className="h-28 rounded-[24px] bg-slate-100" />
                ))}
            </div>

            {Array.from({ length: 4 }).map((_, index) => (
                <div key={`drawer-section-${index}`} className="h-40 rounded-[28px] bg-slate-100" />
            ))}
        </div>
    )
}

function deriveSubOrderItemPresentation(order?: SuperAdminOrderDetails | null) {
    if (!order) {
        return []
    }

    return order.subOrders.map((subOrder: SuperAdminOrderSubOrderDetails) => ({
        subOrder,
        items: order.orderItems.filter((item: SuperAdminOrderItem) => item.subOrderId === subOrder.subOrderId),
    }))
}

export default function OrderDetailsDrawer({
    isOpen,
    order,
    isLoading,
    isError,
    errorMessage,
    onClose,
    onRetry,
}: OrderDetailsDrawerProps) {
    const [showDebugInfo, setShowDebugInfo] = useState(false)

    useEffect(() => {
        if (!isOpen) {
            setShowDebugInfo(false)
            return undefined
        }

        function handleEscape(event: KeyboardEvent) {
            if (event.key === 'Escape') {
                onClose()
            }
        }

        window.addEventListener('keydown', handleEscape)
        return () => window.removeEventListener('keydown', handleEscape)
    }, [isOpen, onClose])

    useEffect(() => {
        setShowDebugInfo(false)
    }, [order?.orderId])

    const orderAge = useMemo(() => getRelativeAge(order?.createdAt), [order?.createdAt])
    const splitItemPresentation = useMemo(
        () => deriveSubOrderItemPresentation(order),
        [order],
    )

    const shouldShowIssueBanner = order
        ? isFailedPaymentStatus(order.paymentStatus) ||
          isPendingOrderStatus(order.status) ||
          (isPendingOrderStatus(order.status) && orderAge.isStuck)
        : false

    return (
        <>
            <div
                className={`fixed inset-0 z-40 bg-slate-950/45 transition-opacity duration-300 ${isOpen ? 'pointer-events-auto opacity-100' : 'pointer-events-none opacity-0'}`}
                onClick={onClose}
            />

            <aside
                className={`fixed right-0 top-0 z-50 flex h-full w-full max-w-[780px] transform flex-col border-l border-slate-200 bg-white shadow-2xl transition-transform duration-300 ${isOpen ? 'translate-x-0' : 'translate-x-full'}`}
                aria-hidden={!isOpen}
            >
                <div className="border-b border-slate-100 px-6 py-5">
                    <div className="flex items-start justify-between gap-4">
                        <div className="min-w-0">
                            <p className="text-xs font-semibold uppercase tracking-[0.24em] text-emerald-600">
                                Order Details
                            </p>
                            <h2 className="mt-2 truncate text-2xl font-semibold text-slate-950">
                                {order?.orderNumber ?? 'Loading order'}
                            </h2>
                            <p className="mt-2 font-mono text-xs text-slate-500">
                                {order?.orderId ?? 'Fetching CommerceHub order record'}
                            </p>
                        </div>

                        <button
                            type="button"
                            className="rounded-2xl border border-slate-200 bg-white p-3 text-slate-500 transition hover:bg-slate-50 hover:text-slate-900"
                            onClick={onClose}
                        >
                            <X className="h-5 w-5" />
                        </button>
                    </div>
                </div>

                <div className="flex-1 overflow-y-auto px-6 py-6">
                    {isLoading ? <DrawerSkeleton /> : null}

                    {!isLoading && isError ? (
                        <div className="rounded-[28px] border border-rose-200 bg-rose-50 p-6">
                            <div className="flex items-start gap-3">
                                <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-rose-100 text-rose-700">
                                    <AlertTriangle className="h-5 w-5" />
                                </div>
                                <div className="min-w-0 flex-1">
                                    <h3 className="text-lg font-semibold text-rose-950">
                                        Unable to load order details
                                    </h3>
                                    <p className="mt-2 text-sm leading-6 text-rose-900/80">
                                        {errorMessage ?? 'The detail request failed before the drawer could hydrate.'}
                                    </p>
                                    <button
                                        type="button"
                                        className="mt-4 inline-flex items-center gap-2 rounded-2xl bg-rose-700 px-4 py-3 text-sm font-semibold text-white transition hover:bg-rose-800"
                                        onClick={onRetry}
                                    >
                                        <Loader2 className="h-4 w-4" />
                                        Retry detail request
                                    </button>
                                </div>
                            </div>
                        </div>
                    ) : null}

                    {!isLoading && !isError && order ? (
                        <div className="space-y-6">
                            {shouldShowIssueBanner ? (
                                <div
                                    className={`rounded-[28px] border px-5 py-4 ${
                                        isFailedPaymentStatus(order.paymentStatus)
                                            ? 'border-rose-200 bg-rose-50 text-rose-950'
                                            : 'border-amber-200 bg-amber-50 text-amber-950'
                                    }`}
                                >
                                    <div className="flex items-start gap-3">
                                        <AlertTriangle className="mt-0.5 h-5 w-5" />
                                        <div>
                                            <p className="text-sm font-semibold">
                                                {isFailedPaymentStatus(order.paymentStatus)
                                                    ? 'Payment issue detected'
                                                    : orderAge.isStuck
                                                        ? `Order appears stuck (${orderAge.label})`
                                                        : 'Order still pending'}
                                            </p>
                                            <p className="mt-1 text-sm leading-6 opacity-85">
                                                {isFailedPaymentStatus(order.paymentStatus)
                                                    ? 'This order is flagged because the payment status is failed or rejected and may need immediate recovery.'
                                                    : orderAge.isStuck
                                                        ? 'The order is still pending after more than two hours, so it likely needs operational follow-up.'
                                                        : 'This order is still in the pending state and may need operational follow-up.'}
                                            </p>
                                        </div>
                                    </div>
                                </div>
                            ) : null}

                            <div className="grid gap-4 sm:grid-cols-2">
                                <DetailCard
                                    label="Total"
                                    value={formatCurrency(order.totalAmount, order.currency)}
                                    tone="border-slate-200 bg-slate-950 text-white"
                                />
                                <DetailCard
                                    label="Vendors"
                                    value={`${order.vendorCount} ${order.vendorCount === 1 ? 'vendor' : 'vendors'}`}
                                    tone="border-emerald-200 bg-emerald-50 text-emerald-950"
                                />
                                <DetailCard
                                    label="Status"
                                    value={order.status}
                                    tone={isPendingOrderStatus(order.status)
                                        ? 'border-amber-200 bg-amber-50 text-amber-950'
                                        : 'border-slate-200 bg-white text-slate-950'}
                                />
                                <DetailCard
                                    label="Payment Status"
                                    value={order.paymentStatus}
                                    tone={isFailedPaymentStatus(order.paymentStatus)
                                        ? 'border-rose-200 bg-rose-50 text-rose-950'
                                        : 'border-sky-200 bg-sky-50 text-sky-950'}
                                />
                            </div>

                            <section className="rounded-[30px] border border-slate-200 bg-slate-50/70 p-5">
                                <div className="flex items-center gap-2 text-slate-900">
                                    <ReceiptText className="h-5 w-5 text-emerald-600" />
                                    <h3 className="text-lg font-semibold">Order Summary</h3>
                                </div>
                                <div className="mt-4 grid gap-4 md:grid-cols-2">
                                    <div className="rounded-[24px] border border-white/70 bg-white p-4 shadow-sm">
                                        <div className="space-y-3 text-sm text-slate-600">
                                            <div className="flex items-center justify-between gap-3">
                                                <span>Tenant summary</span>
                                                <span className="font-semibold text-slate-950">{order.tenantName}</span>
                                            </div>
                                            <div className="flex items-center justify-between gap-3">
                                                <span>Created at</span>
                                                <span className="font-semibold text-slate-950">{formatDateTime(order.createdAt)}</span>
                                            </div>
                                            <div className="flex items-center justify-between gap-3">
                                                <span>Age</span>
                                                <span className="font-semibold text-slate-950">{orderAge.label}</span>
                                            </div>
                                            <div className="flex items-center justify-between gap-3">
                                                <span>Paid at</span>
                                                <span className="font-semibold text-slate-950">{formatDateTime(order.paidAt)}</span>
                                            </div>
                                            <div className="flex items-center justify-between gap-3">
                                                <span>Payment method</span>
                                                <span className="font-semibold text-slate-950">
                                                    {order.paymentMethodName || order.paymentMethodCode || 'Unavailable'}
                                                </span>
                                            </div>
                                            <div className="flex items-center justify-between gap-3">
                                                <span>Gateway</span>
                                                <span className="font-semibold text-slate-950">{order.paymentGateway || 'Unavailable'}</span>
                                            </div>
                                        </div>
                                    </div>

                                    <div className="rounded-[24px] border border-white/70 bg-white p-4 shadow-sm">
                                        <div className="space-y-3 text-sm text-slate-600">
                                            <div className="flex items-center justify-between gap-3">
                                                <span>Subtotal</span>
                                                <span className="font-semibold text-slate-950">{formatCurrency(order.subtotal, order.currency)}</span>
                                            </div>
                                            <div className="flex items-center justify-between gap-3">
                                                <span>Shipping</span>
                                                <span className="font-semibold text-slate-950">{formatCurrency(order.shippingTotal, order.currency)}</span>
                                            </div>
                                            <div className="flex items-center justify-between gap-3 border-t border-slate-100 pt-3">
                                                <span className="font-semibold text-slate-950">Total</span>
                                                <span className="text-base font-semibold text-slate-950">
                                                    {formatCurrency(order.totalAmount, order.currency)}
                                                </span>
                                            </div>
                                            <div className="pt-2 text-xs text-slate-500">
                                                Payment reference: {order.paymentReference ?? order.paymentReferenceId ?? 'Unavailable'}
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            </section>

                            <section className="rounded-[30px] border border-slate-200 bg-white p-5 shadow-sm">
                                <div className="flex items-center gap-2 text-slate-900">
                                    <CircleDollarSign className="h-5 w-5 text-emerald-600" />
                                    <h3 className="text-lg font-semibold">Financial Snapshot</h3>
                                </div>

                                <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                                    <div className="rounded-2xl border border-white/80 bg-slate-50 px-4 py-4">
                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                            Subtotal
                                        </p>
                                        <p className="mt-2 text-sm font-semibold text-slate-950">
                                            {formatCurrency(order.subtotal, order.currency)}
                                        </p>
                                    </div>
                                    <div className="rounded-2xl border border-rose-100 bg-rose-50 px-4 py-4">
                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-rose-700">
                                            Commission
                                        </p>
                                        <p className="mt-2 text-sm font-semibold text-rose-900">
                                            {formatCurrency(order.platformCommissionTotal, order.currency)}
                                        </p>
                                    </div>
                                    <div className="rounded-2xl border border-emerald-100 bg-emerald-50 px-4 py-4">
                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-emerald-700">
                                            Vendor Earnings
                                        </p>
                                        <p className="mt-2 text-sm font-semibold text-emerald-900">
                                            {formatCurrency(order.vendorEarningsTotal, order.currency)}
                                        </p>
                                    </div>
                                    <div className="rounded-2xl border border-white/80 bg-slate-50 px-4 py-4">
                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                            Shipping
                                        </p>
                                        <p className="mt-2 text-sm font-semibold text-slate-950">
                                            {formatCurrency(order.shippingTotal, order.currency)}
                                        </p>
                                    </div>
                                </div>
                            </section>

                            <section className="rounded-[30px] border border-slate-200 bg-white p-5 shadow-sm">
                                <div className="flex items-center gap-2 text-slate-900">
                                    <UserRound className="h-5 w-5 text-sky-600" />
                                    <h3 className="text-lg font-semibold">Customer Info</h3>
                                </div>
                                <div className="mt-4 grid gap-4 md:grid-cols-2">
                                    <div className="rounded-[24px] border border-slate-200 bg-slate-50 p-4">
                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                            Name
                                        </p>
                                        <p className="mt-2 text-sm font-semibold text-slate-950">
                                            {order.customerName || 'Unknown customer'}
                                        </p>
                                        <p className="mt-4 text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                            Phone
                                        </p>
                                        <p className="mt-2 text-sm font-semibold text-slate-950">
                                            {order.customerPhone || 'Unavailable'}
                                        </p>
                                    </div>

                                    <div className="rounded-[24px] border border-slate-200 bg-slate-50 p-4">
                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                            Delivery Address
                                        </p>
                                        <p className="mt-2 text-sm leading-6 text-slate-700">
                                            {[order.addressLine, order.area, order.city].filter(Boolean).join(', ') || 'Unavailable'}
                                        </p>
                                        <p className="mt-4 text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                            Notes
                                        </p>
                                        <p className="mt-2 text-sm leading-6 text-slate-700">
                                            {order.notes || 'No customer notes'}
                                        </p>
                                    </div>
                                </div>
                            </section>

                            <section className="rounded-[30px] border border-slate-200 bg-white p-5 shadow-sm">
                                <div className="flex items-center gap-2 text-slate-900">
                                    <CreditCard className="h-5 w-5 text-violet-600" />
                                    <h3 className="text-lg font-semibold">Payment Details</h3>
                                </div>

                                {order.payments.length === 0 ? (
                                    <div className="mt-4 rounded-[24px] border border-dashed border-slate-300 bg-slate-50 px-5 py-6 text-sm text-slate-500">
                                        No payment records were attached to this order yet.
                                    </div>
                                ) : (
                                    <div className="mt-4 space-y-4">
                                        {order.payments.map((payment) => (
                                            <div key={payment.paymentId} className="rounded-[24px] border border-slate-200 bg-slate-50 p-4">
                                                <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                                                    <div>
                                                        <p className="text-sm font-semibold text-slate-950">
                                                            {payment.provider} | {formatCurrency(payment.amount, payment.currency)}
                                                        </p>
                                                        <p className="mt-1 font-mono text-xs text-slate-500">
                                                            {payment.providerReference}
                                                        </p>
                                                    </div>
                                                    <span className="inline-flex rounded-full border border-slate-200 bg-white px-3 py-1 text-xs font-semibold text-slate-800">
                                                        {payment.status}
                                                    </span>
                                                </div>

                                                <div className="mt-4 grid gap-3 md:grid-cols-3">
                                                    <div className="rounded-2xl border border-white/80 bg-white px-4 py-3">
                                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                            Created
                                                        </p>
                                                        <p className="mt-2 text-sm font-semibold text-slate-950">
                                                            {formatDateTime(payment.createdAt)}
                                                        </p>
                                                    </div>
                                                    <div className="rounded-2xl border border-white/80 bg-white px-4 py-3">
                                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                            Paid
                                                        </p>
                                                        <p className="mt-2 text-sm font-semibold text-slate-950">
                                                            {formatDateTime(payment.paidAtUtc)}
                                                        </p>
                                                    </div>
                                                    <div className="rounded-2xl border border-white/80 bg-white px-4 py-3">
                                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                            Event Processing
                                                        </p>
                                                        <p className="mt-2 text-sm font-semibold text-slate-950">
                                                            {payment.isProcessed ? 'Processed' : 'Pending'}
                                                        </p>
                                                    </div>
                                                </div>

                                                {payment.failureReason ? (
                                                    <div className="mt-4 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-900">
                                                        Failure reason: {payment.failureReason}
                                                    </div>
                                                ) : null}
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </section>

                            <section className="rounded-[30px] border border-slate-200 bg-white p-5 shadow-sm">
                                <div className="flex items-center gap-2 text-slate-900">
                                    <Package className="h-5 w-5 text-sky-600" />
                                    <h3 className="text-lg font-semibold">Items</h3>
                                </div>

                                {splitItemPresentation.length === 0 ? (
                                    <div className="mt-4 rounded-[24px] border border-dashed border-slate-300 bg-slate-50 px-5 py-6 text-sm text-slate-500">
                                        No item lines were attached to this order yet.
                                    </div>
                                ) : (
                                    <div className="mt-4 space-y-4">
                                        {splitItemPresentation.map(({ subOrder, items }) => (
                                            <div key={subOrder.subOrderId} className="rounded-[24px] border border-slate-200 bg-slate-50 p-4">
                                                <div className="flex items-center justify-between gap-3">
                                                    <div>
                                                        <p className="text-sm font-semibold text-slate-950">
                                                            {subOrder.vendorName}
                                                        </p>
                                                        <p className="mt-1 text-xs text-slate-500">
                                                            {subOrder.orderNumber} | {items.length} {items.length === 1 ? 'item' : 'items'}
                                                        </p>
                                                    </div>
                                                </div>

                                                <div className="mt-4 space-y-3">
                                                    {items.map((item) => (
                                                        <div key={item.itemId} className="grid gap-4 rounded-2xl border border-white/80 bg-white p-4 md:grid-cols-[72px_1fr_auto]">
                                                            <div className="flex h-[72px] w-[72px] items-center justify-center overflow-hidden rounded-2xl bg-slate-100">
                                                                {item.imageUrl ? (
                                                                    <img
                                                                        src={item.imageUrl}
                                                                        alt={item.productName}
                                                                        className="h-full w-full object-cover"
                                                                    />
                                                                ) : (
                                                                    <Package className="h-6 w-6 text-slate-400" />
                                                                )}
                                                            </div>

                                                            <div>
                                                                <p className="text-sm font-semibold text-slate-950">
                                                                    {item.productName}
                                                                </p>
                                                                <p className="mt-1 text-sm text-slate-600">
                                                                    {item.variantName || 'Base product'}
                                                                </p>
                                                                <div className="mt-3 flex flex-wrap gap-3 text-xs text-slate-500">
                                                                    <span>Qty: {item.quantity}</span>
                                                                    <span>Unit: {formatCurrency(item.unitPrice, order.currency)}</span>
                                                                </div>
                                                            </div>

                                                            <div className="self-center text-left md:text-right">
                                                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                                    Total
                                                                </p>
                                                                <p className="mt-2 text-sm font-semibold text-slate-950">
                                                                    {formatCurrency(item.totalPrice, order.currency)}
                                                                </p>
                                                            </div>
                                                        </div>
                                                    ))}
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </section>

                            <section className="rounded-[30px] border border-slate-200 bg-white p-5 shadow-sm">
                                <div className="flex items-center gap-2 text-slate-900">
                                    <Store className="h-5 w-5 text-emerald-600" />
                                    <h3 className="text-lg font-semibold">Sub-Orders Breakdown</h3>
                                </div>

                                <div className="mt-4 space-y-4">
                                    {order.subOrders.map((subOrder) => (
                                        <div key={subOrder.subOrderId} className="rounded-[24px] border border-slate-200 bg-slate-50 p-4">
                                            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                                                <div>
                                                    <p className="text-sm font-semibold text-slate-950">
                                                        {subOrder.vendorName}
                                                    </p>
                                                    <p className="mt-1 text-xs text-slate-500">
                                                        {subOrder.orderNumber} | {subOrder.itemCount} {subOrder.itemCount === 1 ? 'item' : 'items'}
                                                    </p>
                                                </div>

                                                <div className="flex flex-wrap gap-2">
                                                    <span className="inline-flex rounded-full border border-slate-200 bg-white px-3 py-1 text-xs font-semibold text-slate-800">
                                                        {subOrder.status}
                                                    </span>
                                                    <span className="inline-flex rounded-full border border-slate-200 bg-white px-3 py-1 text-xs font-semibold text-slate-800">
                                                        {subOrder.paymentStatus}
                                                    </span>
                                                </div>
                                            </div>

                                            <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                                                <div className="rounded-2xl border border-white/80 bg-white px-4 py-3">
                                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                        Amount
                                                    </p>
                                                    <p className="mt-2 text-sm font-semibold text-slate-950">
                                                        {formatCurrency(subOrder.amount, subOrder.currency)}
                                                    </p>
                                                </div>
                                                <div className="rounded-2xl border border-rose-100 bg-rose-50 px-4 py-3">
                                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-rose-700">
                                                        Commission
                                                    </p>
                                                    <p className="mt-2 text-sm font-semibold text-rose-900">
                                                        {formatCurrency(subOrder.platformCommissionAmount, subOrder.currency)}
                                                    </p>
                                                    <p className="mt-1 text-xs text-rose-700">
                                                        {formatPercent(subOrder.platformCommissionRate)}
                                                    </p>
                                                </div>
                                                <div className="rounded-2xl border border-emerald-100 bg-emerald-50 px-4 py-3">
                                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-emerald-700">
                                                        Vendor Earnings
                                                    </p>
                                                    <p className="mt-2 text-sm font-semibold text-emerald-900">
                                                        {formatCurrency(subOrder.vendorEarningAmount, subOrder.currency)}
                                                    </p>
                                                </div>
                                                <div className="rounded-2xl border border-white/80 bg-white px-4 py-3">
                                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                        Payment Method
                                                    </p>
                                                    <p className="mt-2 text-sm font-semibold text-slate-950">
                                                        {subOrder.paymentMethodName || 'Unavailable'}
                                                    </p>
                                                    <p className="mt-1 text-xs text-slate-500">
                                                        {subOrder.shippingMethodName || 'No shipping method'}
                                                    </p>
                                                </div>
                                            </div>

                                            <div className="mt-4 flex flex-wrap items-center gap-3 text-xs text-slate-500">
                                                <span>Payment reference: {subOrder.paymentReferenceId || 'Unavailable'}</span>
                                                <span className="hidden text-slate-300 sm:inline">|</span>
                                                <span>Paid: {formatDateTime(subOrder.paidAt)}</span>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            </section>

                            <section className="rounded-[30px] border border-slate-200 bg-white p-5 shadow-sm">
                                <div className="flex items-center justify-between gap-4">
                                    <div className="flex items-center gap-2 text-slate-900">
                                        <Bug className="h-5 w-5 text-amber-600" />
                                        <h3 className="text-lg font-semibold">Debug Info</h3>
                                    </div>

                                    <button
                                        type="button"
                                        className="inline-flex items-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                        onClick={() => setShowDebugInfo((current) => !current)}
                                    >
                                        {showDebugInfo ? 'Hide Debug Info' : 'Show Debug Info'}
                                        {showDebugInfo ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
                                    </button>
                                </div>

                                <p className="mt-3 text-sm leading-6 text-slate-500">
                                    Raw identifiers and commission snapshots help investigate projection drift or payment-processing issues without leaving the drawer.
                                </p>

                                {showDebugInfo ? (
                                    <div className="mt-4 space-y-4">
                                        <div className="grid gap-4 md:grid-cols-3">
                                            <div className="rounded-[24px] border border-slate-200 bg-slate-50 p-4">
                                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                    Event Processing
                                                </p>
                                                <p className="mt-2 text-sm font-semibold text-slate-950">
                                                    {order.eventStatus || 'Unavailable'}
                                                </p>
                                            </div>
                                            <div className="rounded-[24px] border border-slate-200 bg-slate-50 p-4">
                                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                    Payment IDs
                                                </p>
                                                <div className="mt-2 space-y-1">
                                                    {order.payments.length === 0 ? (
                                                        <p className="text-sm text-slate-500">Unavailable</p>
                                                    ) : (
                                                        order.payments.map((payment) => (
                                                            <p key={payment.paymentId} className="break-all font-mono text-xs text-slate-700">
                                                                {payment.paymentId}
                                                            </p>
                                                        ))
                                                    )}
                                                </div>
                                            </div>
                                            <div className="rounded-[24px] border border-slate-200 bg-slate-50 p-4">
                                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                    Sub-Order IDs
                                                </p>
                                                <div className="mt-2 space-y-1">
                                                    {order.subOrders.length === 0 ? (
                                                        <p className="text-sm text-slate-500">Unavailable</p>
                                                    ) : (
                                                        order.subOrders.map((subOrder) => (
                                                            <p key={subOrder.subOrderId} className="break-all font-mono text-xs text-slate-700">
                                                                {subOrder.subOrderId}
                                                            </p>
                                                        ))
                                                    )}
                                                </div>
                                            </div>
                                        </div>

                                        <div className="space-y-3">
                                            {order.subOrders
                                                .filter((subOrder) => subOrder.commissionSnapshotJson)
                                                .map((subOrder) => (
                                                    <div key={`debug-${subOrder.subOrderId}`} className="rounded-[24px] border border-slate-200 bg-slate-950 p-4 text-white">
                                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-300">
                                                            Commission Snapshot | {subOrder.vendorName}
                                                        </p>
                                                        <pre className="mt-3 overflow-x-auto whitespace-pre-wrap break-words text-xs leading-6 text-slate-100">
                                                            {formatJson(subOrder.commissionSnapshotJson)}
                                                        </pre>
                                                    </div>
                                                ))}

                                            {order.subOrders.every((subOrder) => !subOrder.commissionSnapshotJson) ? (
                                                <div className="rounded-[24px] border border-dashed border-slate-300 bg-slate-50 px-5 py-6 text-sm text-slate-500">
                                                    No commission snapshot JSON is available for this order.
                                                </div>
                                            ) : null}
                                        </div>
                                    </div>
                                ) : null}
                            </section>

                            <section className="rounded-[30px] border border-slate-200 bg-white p-5 shadow-sm">
                                <div className="flex items-center gap-2 text-slate-900">
                                    <Clock3 className="h-5 w-5 text-emerald-600" />
                                    <h3 className="text-lg font-semibold">Operational Notes</h3>
                                </div>
                                <p className="mt-4 text-sm leading-7 text-slate-600">
                                    This drawer is loaded on demand so the main monitoring table stays light. Summary cards refresh independently on the page, while item and debug sections come directly from the current order projection for deeper inspection.
                                </p>
                            </section>
                        </div>
                    ) : null}
                </div>
            </aside>
        </>
    )
}

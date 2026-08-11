import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { AlertCircle, Package, RefreshCw, X } from 'lucide-react'
import { downloadAdminOrderCheckoutAttachment, getAdminOrderDetails, getAdminOrders, updateAdminOrderStatus } from '../services/adminOrdersApi'
import { presentOrderCheckoutFields } from './orderCheckoutFieldPresentation'
import { useAuthStore } from '../store/authStore'
import {
    requireBackendAffiliateAttributionAuthority,
    requireBackendMarketplaceLineageAuthority,
    requireBackendOrderSourceAuthority,
    requireCurrentVendorOrderVisibilityAuthority,
    requireCurrentVendorOrderVisibilityListAuthority,
} from '../auth/commerceHubVendorIdentityAuthority'

const PAGE_SIZE_OPTIONS = [20, 50]
const ORDER_STATUS_OPTIONS = [
    '',
    'Pending',
    'Confirmed',
    'Processing',
    'Shipped',
    'Delivered',
    'Completed',
    'Cancelled',
    'Refunded',
    'OnHold',
]
const PAYMENT_STATUS_OPTIONS = [
    '',
    'Pending',
    'Authorized',
    'Collected',
    'Paid',
    'Failed',
    'Refunded',
    'AwaitingVerification',
    'Rejected',
    'Settled',
    'CashOnDelivery',
]
const SORT_OPTIONS = [
    { value: 'createdAt:desc', label: 'Newest first' },
    { value: 'createdAt:asc', label: 'Oldest first' },
    { value: 'orderNumber:asc', label: 'Order number A-Z' },
    { value: 'orderNumber:desc', label: 'Order number Z-A' },
    { value: 'customerName:asc', label: 'Customer A-Z' },
    { value: 'totalAmount:desc', label: 'Total high-low' },
    { value: 'totalAmount:asc', label: 'Total low-high' },
    { value: 'status:asc', label: 'Status A-Z' },
    { value: 'paymentStatus:asc', label: 'Payment A-Z' },
]

function parsePositiveInt(value, fallback) {
    const parsed = Number.parseInt(value ?? '', 10)
    return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

function formatCurrency(amount, currency) {
    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: currency || 'EGP',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(Number(amount || 0))
}

function getOrderSourcePresentation(order) {
    return requireBackendOrderSourceAuthority(order)
}

function getAffiliateAttributionPresentation(order) {
    return requireBackendAffiliateAttributionAuthority(order)
}

function getMarketplaceLineagePresentation(order) {
    return order?.isMarketplaceOrder ? requireBackendMarketplaceLineageAuthority(order) : null
}

function formatDateTime(value) {
    if (!value) {
        return 'Unavailable'
    }

    const parsed = new Date(value)
    if (Number.isNaN(parsed.getTime())) {
        return 'Unavailable'
    }

    return parsed.toLocaleString()
}

function normalizeFilters(searchParams) {
    return {
        page: parsePositiveInt(searchParams.get('page'), 1),
        pageSize: parsePositiveInt(searchParams.get('pageSize'), 20),
        status: searchParams.get('status') || '',
        paymentStatus: searchParams.get('paymentStatus') || '',
        fromDate: searchParams.get('fromDate') || '',
        toDate: searchParams.get('toDate') || '',
        search: searchParams.get('search') || '',
        sortBy: searchParams.get('sortBy') || 'createdAt',
        sortDirection: searchParams.get('sortDirection') || 'desc',
    }
}

function StatusBadge({ value, tone = 'slate' }) {
    const palette = {
        slate: 'border-slate-200 bg-slate-100 text-slate-700',
        emerald: 'border-emerald-200 bg-emerald-100 text-emerald-700',
        amber: 'border-amber-200 bg-amber-100 text-amber-700',
        rose: 'border-rose-200 bg-rose-100 text-rose-700',
        sky: 'border-sky-200 bg-sky-100 text-sky-700',
    }

    return (
        <span className={`inline-flex rounded-full border px-2.5 py-1 text-xs font-semibold ${palette[tone] || palette.slate}`}>
            {value || 'Unknown'}
        </span>
    )
}

function resolveOrderTone(status) {
    switch (status) {
        case 'Delivered':
        case 'Completed':
            return 'emerald'
        case 'Pending':
        case 'OnHold':
            return 'amber'
        case 'Cancelled':
        case 'Refunded':
            return 'rose'
        case 'Shipped':
        case 'Processing':
        case 'Confirmed':
            return 'sky'
        default:
            return 'slate'
    }
}

function resolvePaymentTone(status) {
    switch (status) {
        case 'Paid':
        case 'Collected':
        case 'Settled':
            return 'emerald'
        case 'Pending':
        case 'Authorized':
        case 'AwaitingVerification':
        case 'CashOnDelivery':
            return 'amber'
        case 'Failed':
        case 'Rejected':
        case 'Refunded':
            return 'rose'
        default:
            return 'slate'
    }
}

function FilterField({ label, children }) {
    return (
        <label className="flex flex-col gap-2 text-sm text-slate-700">
            <span className="font-medium">{label}</span>
            {children}
        </label>
    )
}

function SectionCard({ title, children }) {
    return (
        <section className="rounded-3xl border border-slate-200 bg-white p-5 shadow-sm">
            <h3 className="text-sm font-semibold uppercase tracking-[0.18em] text-slate-500">{title}</h3>
            <div className="mt-4">{children}</div>
        </section>
    )
}

function OrderDetailsPanel({ order, isLoading, isError, error, onClose, onRetry, onStatusChange, isStatusUpdating, statusUpdateError }) {
    const { t } = useTranslation()
    const [statusCandidate, setStatusCandidate] = useState(order?.status || '')
    const [statusReason, setStatusReason] = useState('')

    const canSubmitStatusCandidate = Boolean(order?.rowVersion && statusCandidate)
    const checkoutFieldItems = presentOrderCheckoutFields(order?.checkoutFieldSnapshots)
    const downloadAttachmentMutation = useMutation({
        mutationFn: ({ attachmentId, fileName }) => downloadAdminOrderCheckoutAttachment(order.orderId, attachmentId, fileName),
    })

    function handleSubmitStatus(event) {
        event.preventDefault()
        if (!canSubmitStatusCandidate) {
            return
        }

        onStatusChange({
            newStatus: statusCandidate,
            reason: statusReason,
            rowVersion: order.rowVersion,
        })
    }

    useEffect(() => {
        setStatusCandidate(order?.status || '')
        setStatusReason('')
    }, [order?.orderId, order?.status])

    return (
        <aside className="fixed inset-y-0 right-0 z-40 w-full max-w-2xl border-l border-slate-200 bg-white shadow-2xl">
            <div className="flex h-full flex-col">
                <div className="flex items-start justify-between border-b border-slate-200 px-6 py-5">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Order details</p>
                        <h2 className="mt-2 text-xl font-semibold text-slate-950">
                            {order?.orderNumber || 'Loading order'}
                        </h2>
                    </div>
                    <button
                        type="button"
                        onClick={onClose}
                        className="rounded-full border border-slate-200 p-2 text-slate-500 transition hover:border-slate-300 hover:text-slate-900"
                        aria-label="Close order details"
                    >
                        <X className="h-4 w-4" />
                    </button>
                </div>

                <div className="flex-1 overflow-y-auto px-6 py-5">
                    {isLoading ? (
                        <div className="space-y-4">
                            <div className="h-24 animate-pulse rounded-3xl bg-slate-100" />
                            <div className="h-56 animate-pulse rounded-3xl bg-slate-100" />
                            <div className="h-40 animate-pulse rounded-3xl bg-slate-100" />
                        </div>
                    ) : null}

                    {isError ? (
                        <div className="rounded-3xl border border-rose-200 bg-rose-50 p-5 text-sm text-rose-700">
                            <p className="font-semibold">Unable to load the selected order.</p>
                            <p className="mt-2">{error?.message || 'Please try again.'}</p>
                            <button
                                type="button"
                                onClick={onRetry}
                                className="mt-4 inline-flex items-center gap-2 rounded-full border border-rose-200 bg-white px-4 py-2 text-sm font-semibold text-rose-700 transition hover:border-rose-300"
                            >
                                <RefreshCw className="h-4 w-4" />
                                Retry
                            </button>
                        </div>
                    ) : null}

                    {!isLoading && !isError && order ? (
                        <div className="space-y-5">
                            <SectionCard title="Lifecycle">
                                <div className="grid gap-4 md:grid-cols-2">
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Order status</p>
                                        <div className="mt-2">
                                            <StatusBadge value={order.status} tone={resolveOrderTone(order.status)} />
                                        </div>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Payment status</p>
                                        <div className="mt-2">
                                            <StatusBadge value={order.paymentStatus} tone={resolvePaymentTone(order.paymentStatus)} />
                                        </div>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Shipment status</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{order.shipmentStatus || 'No shipment'}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Created</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{formatDateTime(order.createdAtUtc)}</p>
                                    </div>
                                </div>

                                <form className="mt-5 rounded-2xl border border-slate-200 bg-slate-50 p-4" onSubmit={handleSubmitStatus}>
                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                        Request status change
                                    </p>
                                    <div className="mt-3 grid gap-3 md:grid-cols-[1fr_1.4fr_auto] md:items-end">
                                        <label className="flex flex-col gap-2 text-sm text-slate-700">
                                            <span className="font-medium">Requested status</span>
                                            <select
                                                value={statusCandidate}
                                                onChange={(event) => setStatusCandidate(event.target.value)}
                                                disabled={!order.rowVersion || isStatusUpdating}
                                                className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100 disabled:cursor-not-allowed disabled:bg-slate-100"
                                            >
                                                {ORDER_STATUS_OPTIONS.filter(Boolean).map((option) => (
                                                    <option key={option} value={option}>{option}</option>
                                                ))}
                                            </select>
                                        </label>
                                        <label className="flex flex-col gap-2 text-sm text-slate-700">
                                            <span className="font-medium">Reason</span>
                                            <input
                                                value={statusReason}
                                                onChange={(event) => setStatusReason(event.target.value)}
                                                disabled={!order.rowVersion || isStatusUpdating}
                                                placeholder="Optional backend audit note"
                                                className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100 disabled:cursor-not-allowed disabled:bg-slate-100"
                                            />
                                        </label>
                                        <button
                                            type="submit"
                                            disabled={!canSubmitStatusCandidate || isStatusUpdating}
                                            className="inline-flex items-center justify-center rounded-2xl bg-slate-950 px-4 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:bg-slate-300"
                                        >
                                            {isStatusUpdating ? 'Submitting' : 'Submit candidate'}
                                        </button>
                                    </div>
                                    {!order.rowVersion ? (
                                        <p className="mt-3 text-sm text-amber-700">
                                            Status mutation is withheld until the backend returns RowVersion evidence.
                                        </p>
                                    ) : null}
                                    {statusUpdateError ? (
                                        <p className="mt-3 text-sm text-rose-700">{statusUpdateError}</p>
                                    ) : null}
                                </form>
                            </SectionCard>

                            <SectionCard title="Customer">
                                <div className="grid gap-4 md:grid-cols-2">
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Name</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{order.customer?.name || 'Unavailable'}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Email</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{order.customer?.email || 'Unavailable'}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Phone</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{order.customer?.phone || 'Unavailable'}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Order source</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{getOrderSourcePresentation(order).label}</p>
                                    </div>
                                </div>
                            </SectionCard>

                            {getMarketplaceLineagePresentation(order) ? (
                                <SectionCard title="Marketplace Lineage">
                                    {(() => {
                                        const lineage = getMarketplaceLineagePresentation(order)
                                        return (
                                            <div className="grid gap-4 md:grid-cols-2">
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Marketplace</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{lineage.marketplaceName}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Marketplace order</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{lineage.marketplaceOrderNumber}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Marketplace identity</p>
                                                    <p className="mt-2 break-all text-sm font-medium text-slate-900">{lineage.marketplaceOrderId}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Vendor identity</p>
                                                    <p className="mt-2 break-all text-sm font-medium text-slate-900">{lineage.vendorId}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Vendor</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{lineage.vendorName}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Source type</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{lineage.marketplaceSourceType}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Marketplace status</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{lineage.marketplaceStatus}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Payment metadata</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{lineage.marketplacePaymentStatus}</p>
                                                </div>
                                                {lineage.externalCustomerId ? (
                                                    <div className="md:col-span-2">
                                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">External customer identity</p>
                                                        <p className="mt-2 break-all text-sm font-medium text-slate-900">{lineage.externalCustomerId}</p>
                                                    </div>
                                                ) : null}
                                            </div>
                                        )
                                    })()}
                                </SectionCard>
                            ) : null}

                            {getAffiliateAttributionPresentation(order) ? (
                                <SectionCard title="Affiliate Attribution">
                                    {(() => {
                                        const attribution = getAffiliateAttributionPresentation(order)
                                        return (
                                            <div className="grid gap-4 md:grid-cols-2">
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Affiliate identity</p>
                                                    <p className="mt-2 break-all text-sm font-medium text-slate-900">{attribution.affiliateAccountId}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Referral code</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{attribution.referralCode}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Attribution source</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{attribution.source}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Attributed</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{formatDateTime(attribution.attributedAtUtc)}</p>
                                                </div>
                                                {attribution.promotionLinkId ? (
                                                    <div className="md:col-span-2">
                                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Promotion link</p>
                                                        <p className="mt-2 break-all text-sm font-medium text-slate-900">{attribution.promotionLinkId}</p>
                                                    </div>
                                                ) : null}
                                            </div>
                                        )
                                    })()}
                                </SectionCard>
                            ) : null}

                            <SectionCard title="Totals">
                                <div className="grid gap-4 md:grid-cols-2">
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Subtotal</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{formatCurrency(order.totals?.subtotal, order.totals?.currency)}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Discount</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{formatCurrency(order.totals?.discountAmount, order.totals?.currency)}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Tax</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{formatCurrency(order.totals?.taxAmount, order.totals?.currency)}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Shipping</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{formatCurrency(order.totals?.shippingCost, order.totals?.currency)}</p>
                                    </div>
                                    <div className="md:col-span-2">
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Total</p>
                                        <p className="mt-2 text-lg font-semibold text-slate-950">{formatCurrency(order.totals?.total, order.totals?.currency)}</p>
                                    </div>
                                </div>
                            </SectionCard>

                            <SectionCard title="Items">
                                {order.items?.length ? (
                                    <div className="space-y-3">
                                        {order.items.map((item) => (
                                            <div key={item.itemId} className="rounded-2xl border border-slate-200 px-4 py-4">
                                                <div className="flex items-start justify-between gap-4">
                                                    <div>
                                                        <p className="font-medium text-slate-950">{item.productName}</p>
                                                        <p className="mt-1 text-sm text-slate-500">
                                                            {item.variantName || item.sku || 'No variant'}
                                                        </p>
                                                    </div>
                                                    <div className="text-right">
                                                        <p className="text-sm font-semibold text-slate-950">
                                                            {formatCurrency(item.total, order.totals?.currency)}
                                                        </p>
                                                        <p className="mt-1 text-xs text-slate-500">Qty {item.quantity}</p>
                                                    </div>
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                ) : (
                                    <p className="text-sm text-slate-500">No line items were returned for this order.</p>
                                )}
                            </SectionCard>

                            <SectionCard title="Payments">
                                {order.payments?.length ? (
                                    <div className="space-y-3">
                                        {order.payments.map((payment) => (
                                            <div key={payment.paymentIntentId || payment.paymentId} className="rounded-2xl border border-slate-200 px-4 py-4">
                                                <div className="flex flex-wrap items-start justify-between gap-3">
                                                    <div>
                                                        <p className="font-medium text-slate-950">
                                                            {payment.provider || payment.paymentMethod || 'Payment'}
                                                        </p>
                                                        <p className="mt-1 text-sm text-slate-500">
                                                            {payment.paymentReference || payment.providerReference || 'No reference'}
                                                        </p>
                                                    </div>
                                                    <div className="text-right">
                                                        <StatusBadge
                                                            value={payment.status}
                                                            tone={resolvePaymentTone(payment.status)}
                                                        />
                                                        <p className="mt-2 text-sm font-semibold text-slate-950">
                                                            {formatCurrency(payment.amount, payment.currency)}
                                                        </p>
                                                    </div>
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                ) : (
                                    <p className="text-sm text-slate-500">No payment records were returned for this order.</p>
                                )}
                            </SectionCard>

                            <SectionCard title="Addresses and notes">
                                <div className="space-y-4 text-sm text-slate-700">
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Shipping address</p>
                                        <p className="mt-2">
                                            {[
                                                order.shippingAddress?.address,
                                                order.shippingAddress?.city,
                                                order.shippingAddress?.state,
                                                order.shippingAddress?.country,
                                            ].filter(Boolean).join(', ') || 'Unavailable'}
                                        </p>
                                    </div>
                                    {order.customerNotes ? (
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Customer notes</p>
                                            <p className="mt-2 whitespace-pre-wrap">{order.customerNotes}</p>
                                        </div>
                                    ) : null}
                                    {order.adminNotes ? (
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Admin notes</p>
                                            <p className="mt-2 whitespace-pre-wrap">{order.adminNotes}</p>
                                        </div>
                                    ) : null}
                                    {order.cancellationReason ? (
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Cancellation reason</p>
                                            <p className="mt-2 whitespace-pre-wrap">{order.cancellationReason}</p>
                                        </div>
                                    ) : null}
                                </div>
                            </SectionCard>

                            {checkoutFieldItems.length > 0 ? (
                                <SectionCard title="Checkout details">
                                    <dl className="space-y-4">
                                        {checkoutFieldItems.map((item) => (
                                            <div key={item.fieldId}>
                                                <dt className="text-xs uppercase tracking-[0.18em] text-slate-500">{item.label}</dt>
                                                <dd className="mt-2 whitespace-pre-wrap text-sm font-medium text-slate-900">{item.displayValue}</dd>
                                                {item.attachments.map((attachment) => (
                                                    <div key={attachment.attachmentId} className="mt-2 flex flex-wrap items-center gap-2 text-xs text-slate-500">
                                                        <span>
                                                            {attachment.contentType || 'File'}{attachment.sizeBytes > 0 ? `, ${attachment.sizeBytes} bytes` : ''}
                                                        </span>
                                                        <button
                                                            type="button"
                                                            onClick={() => downloadAttachmentMutation.mutate({
                                                                attachmentId: attachment.attachmentId,
                                                                fileName: attachment.fileName,
                                                            })}
                                                            disabled={downloadAttachmentMutation.isPending}
                                                            className="rounded-full border border-slate-200 px-3 py-1 font-semibold text-slate-700 transition hover:border-slate-300 hover:text-slate-950 disabled:cursor-not-allowed disabled:opacity-60"
                                                        >
                                                            {t('checkoutFields.viewDownload', 'View / Download')}
                                                        </button>
                                                    </div>
                                                ))}
                                                {downloadAttachmentMutation.isError ? (
                                                    <p className="mt-2 text-xs text-rose-600">
                                                        {downloadAttachmentMutation.error?.message || t('checkoutFields.downloadFailed', 'Unable to download this attachment right now.')}
                                                    </p>
                                                ) : null}
                                            </div>
                                        ))}
                                    </dl>
                                </SectionCard>
                            ) : null}
                        </div>
                    ) : null}
                </div>
            </div>
        </aside>
    )
}

export default function Orders() {
    const user = useAuthStore((state) => state.user)
    const { orderId: routeOrderId } = useParams()
    const location = useLocation()
    const navigate = useNavigate()
    const queryClient = useQueryClient()
    const [searchParams, setSearchParams] = useSearchParams()
    const filters = useMemo(() => normalizeFilters(searchParams), [searchParams])
    const selectedOrderId = routeOrderId || searchParams.get('orderId')
    const authorityScope = ['pos-admin-orders', user?.tenantId ?? 'anonymous', user?.id ?? 'anonymous']

    const ordersQuery = useQuery({
        queryKey: [...authorityScope, 'list', filters],
        queryFn: () => getAdminOrders(filters),
        staleTime: 30_000,
    })

    const detailsQuery = useQuery({
        queryKey: [...authorityScope, 'details', selectedOrderId],
        queryFn: () => getAdminOrderDetails(selectedOrderId),
        enabled: Boolean(selectedOrderId),
        staleTime: 30_000,
    })

    let admittedOrderList = null
    let orderListAuthorityError = null
    if (!ordersQuery.isFetching && ordersQuery.data) {
        try {
            admittedOrderList = requireCurrentVendorOrderVisibilityListAuthority(ordersQuery.data, user)
        } catch (error) {
            orderListAuthorityError = error
        }
    }

    const orders = admittedOrderList?.items || []
    const totalCount = admittedOrderList?.totalCount || 0
    const totalPages = admittedOrderList?.totalPages || 0

    let selectedOrder = null
    let selectedOrderAuthorityError = null
    if (!detailsQuery.isFetching && detailsQuery.data && selectedOrderId) {
        try {
            selectedOrder = requireCurrentVendorOrderVisibilityAuthority(detailsQuery.data, user, selectedOrderId)
        } catch (error) {
            selectedOrderAuthorityError = error
        }
    }
    const statusMutation = useMutation({
        mutationFn: (candidate) => updateAdminOrderStatus(selectedOrderId, candidate),
        onSuccess: async () => {
            await Promise.all([
                queryClient.invalidateQueries({ queryKey: [...authorityScope, 'list'] }),
                queryClient.invalidateQueries({ queryKey: [...authorityScope, 'details', selectedOrderId] }),
            ])
        },
    })

    function updateParams(updates) {
        setSearchParams((previous) => {
            const next = new URLSearchParams(previous)

            Object.entries(updates).forEach(([key, value]) => {
                if (!value) {
                    next.delete(key)
                    return
                }

                next.set(key, value)
            })

            return next
        })
    }

    function handleFilterChange(key, value) {
        updateParams({
            [key]: value || null,
            page: '1',
        })
    }

    function handleSortChange(value) {
        const [sortBy, sortDirection] = value.split(':')
        updateParams({
            sortBy: sortBy || 'createdAt',
            sortDirection: sortDirection || 'desc',
            page: '1',
        })
    }

    function handlePageChange(nextPage) {
        updateParams({
            page: String(nextPage),
        })
    }

    function handleOpenOrder(nextOrderId) {
        const nextSearch = new URLSearchParams(location.search)
        nextSearch.delete('orderId')
        navigate(`/ecommerce/orders/${nextOrderId}${nextSearch.toString() ? `?${nextSearch.toString()}` : ''}`)
    }

    function handleCloseDetails() {
        if (routeOrderId) {
            navigate(`/ecommerce/orders${location.search}`, { replace: true })
            return
        }

        const nextSearch = new URLSearchParams(location.search)
        nextSearch.delete('orderId')
        navigate(`/ecommerce/orders${nextSearch.toString() ? `?${nextSearch.toString()}` : ''}`, { replace: true })
    }

    return (
        <div className="space-y-6">
            <section className="rounded-[32px] border border-slate-200 bg-white px-6 py-6 shadow-sm">
                <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                    <div>
                        <div className="inline-flex items-center gap-2 rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-sky-700">
                            <Package className="h-3.5 w-3.5" />
                            Storefront order authority
                        </div>
                        <h1 className="mt-4 text-3xl font-semibold tracking-tight text-slate-950">Orders</h1>
                        <p className="mt-2 max-w-3xl text-sm leading-6 text-slate-600">
                            Tenant-scoped order review for storefront and marketplace sub-orders. This view stays inside POS authority and uses the tenant-admin orders API instead of the super-admin cross-tenant route.
                        </p>
                    </div>
                    <button
                        type="button"
                        onClick={() => ordersQuery.refetch()}
                        className="inline-flex items-center justify-center gap-2 rounded-full border border-slate-200 bg-white px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:text-slate-950"
                    >
                        <RefreshCw className={`h-4 w-4 ${ordersQuery.isFetching ? 'animate-spin' : ''}`} />
                        Refresh
                    </button>
                </div>

                <div className="mt-6 grid gap-4 md:grid-cols-2 xl:grid-cols-6">
                    <FilterField label="Search">
                        <input
                            type="search"
                            value={filters.search}
                            onChange={(event) => handleFilterChange('search', event.target.value)}
                            placeholder="Order, customer, email, phone"
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        />
                    </FilterField>

                    <FilterField label="Order status">
                        <select
                            value={filters.status}
                            onChange={(event) => handleFilterChange('status', event.target.value)}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        >
                            {ORDER_STATUS_OPTIONS.map((option) => (
                                <option key={option || 'all'} value={option}>
                                    {option || 'All statuses'}
                                </option>
                            ))}
                        </select>
                    </FilterField>

                    <FilterField label="Payment status">
                        <select
                            value={filters.paymentStatus}
                            onChange={(event) => handleFilterChange('paymentStatus', event.target.value)}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        >
                            {PAYMENT_STATUS_OPTIONS.map((option) => (
                                <option key={option || 'all'} value={option}>
                                    {option || 'All payment states'}
                                </option>
                            ))}
                        </select>
                    </FilterField>

                    <FilterField label="From date">
                        <input
                            type="date"
                            value={filters.fromDate}
                            onChange={(event) => handleFilterChange('fromDate', event.target.value)}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        />
                    </FilterField>

                    <FilterField label="To date">
                        <input
                            type="date"
                            value={filters.toDate}
                            onChange={(event) => handleFilterChange('toDate', event.target.value)}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        />
                    </FilterField>

                    <FilterField label="Page size">
                        <select
                            value={String(filters.pageSize)}
                            onChange={(event) => handleFilterChange('pageSize', event.target.value)}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        >
                            {PAGE_SIZE_OPTIONS.map((option) => (
                                <option key={option} value={option}>
                                    {option} rows
                                </option>
                            ))}
                        </select>
                    </FilterField>

                    <FilterField label="Sort">
                        <select
                            value={`${filters.sortBy}:${filters.sortDirection}`}
                            onChange={(event) => handleSortChange(event.target.value)}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        >
                            {SORT_OPTIONS.map((option) => (
                                <option key={option.value} value={option.value}>
                                    {option.label}
                                </option>
                            ))}
                        </select>
                    </FilterField>
                </div>
            </section>

            {ordersQuery.isError || orderListAuthorityError ? (
                <section className="rounded-[32px] border border-rose-200 bg-rose-50 px-6 py-8 shadow-sm">
                    <div className="flex items-start gap-3">
                        <AlertCircle className="mt-0.5 h-5 w-5 text-rose-600" />
                        <div>
                            <h2 className="text-lg font-semibold text-rose-900">Unable to load orders</h2>
                            <p className="mt-2 text-sm text-rose-700">
                                {ordersQuery.error?.message || orderListAuthorityError?.message || 'Please retry after the latest session and tenant state are restored.'}
                            </p>
                        </div>
                    </div>
                </section>
            ) : (
                <section className="rounded-[32px] border border-slate-200 bg-white shadow-sm">
                    <div className="flex flex-col gap-3 border-b border-slate-200 px-6 py-5 sm:flex-row sm:items-center sm:justify-between">
                        <div>
                            <p className="text-sm font-semibold text-slate-950">{totalCount.toLocaleString()} orders</p>
                            <p className="mt-1 text-sm text-slate-500">
                                SQL-backed tenant order list with storefront and marketplace rows combined.
                            </p>
                        </div>
                        {selectedOrderId ? (
                            <Link
                                to={`/ecommerce/orders${location.search}`}
                                className="inline-flex items-center gap-2 text-sm font-semibold text-sky-700 hover:text-sky-900"
                            >
                                Clear selected order
                            </Link>
                        ) : null}
                    </div>

                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-slate-200">
                            <thead className="bg-slate-50">
                                <tr className="text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                    <th className="px-6 py-4">Order</th>
                                    <th className="px-6 py-4">Customer</th>
                                    <th className="px-6 py-4">Source</th>
                                    <th className="px-6 py-4">Total</th>
                                    <th className="px-6 py-4">Order status</th>
                                    <th className="px-6 py-4">Payment</th>
                                    <th className="px-6 py-4">Created</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-slate-100 bg-white">
                                {ordersQuery.isLoading ? (
                                    Array.from({ length: 6 }).map((_, index) => (
                                        <tr key={`loading-${index}`}>
                                            <td className="px-6 py-5" colSpan={7}>
                                                <div className="h-8 animate-pulse rounded-2xl bg-slate-100" />
                                            </td>
                                        </tr>
                                    ))
                                ) : null}

                                {!ordersQuery.isLoading && !orders.length ? (
                                    <tr>
                                        <td colSpan={7} className="px-6 py-14 text-center text-sm text-slate-500">
                                            No orders matched the current tenant filters.
                                        </td>
                                    </tr>
                                ) : null}

                                {!ordersQuery.isLoading
                                    ? orders.map((order) => {
                                        const sourcePresentation = getOrderSourcePresentation(order)
                                        const affiliateAttribution = getAffiliateAttributionPresentation(order)
                                        return (
                                        <tr
                                            key={order.orderId}
                                            className="cursor-pointer transition hover:bg-sky-50/60"
                                            onClick={() => handleOpenOrder(order.orderId)}
                                        >
                                            <td className="px-6 py-5">
                                                <div className="font-medium text-slate-950">{order.orderNumber}</div>
                                                <div className="mt-1 text-xs text-slate-500">{order.orderId}</div>
                                            </td>
                                            <td className="px-6 py-5 text-sm text-slate-700">{order.customerName || 'Guest checkout'}</td>
                                            <td className="px-6 py-5">
                                                <div className="flex flex-col gap-2">
                                                    <StatusBadge
                                                        value={sourcePresentation.label}
                                                        tone={sourcePresentation.tone}
                                                    />
                                                    {affiliateAttribution ? (
                                                        <StatusBadge
                                                            value={`Affiliate ${affiliateAttribution.referralCode}`}
                                                            tone="emerald"
                                                        />
                                                    ) : null}
                                                </div>
                                            </td>
                                            <td className="px-6 py-5 text-sm font-semibold text-slate-950">
                                                {formatCurrency(order.totalAmount, order.currency)}
                                            </td>
                                            <td className="px-6 py-5">
                                                <StatusBadge value={order.status} tone={resolveOrderTone(order.status)} />
                                            </td>
                                            <td className="px-6 py-5">
                                                <StatusBadge value={order.paymentStatus} tone={resolvePaymentTone(order.paymentStatus)} />
                                            </td>
                                            <td className="px-6 py-5 text-sm text-slate-600">{formatDateTime(order.createdAtUtc)}</td>
                                        </tr>
                                        )
                                    })
                                    : null}
                            </tbody>
                        </table>
                    </div>

                    <div className="flex flex-col gap-3 border-t border-slate-200 px-6 py-4 sm:flex-row sm:items-center sm:justify-between">
                        <p className="text-sm text-slate-500">
                            Page {ordersQuery.data?.page || filters.page} of {Math.max(totalPages, 1)}
                        </p>
                        <div className="flex items-center gap-2">
                            <button
                                type="button"
                                onClick={() => handlePageChange(Math.max(1, filters.page - 1))}
                                disabled={filters.page <= 1}
                                className="rounded-full border border-slate-200 px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:text-slate-950 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                Previous
                            </button>
                            <button
                                type="button"
                                onClick={() => handlePageChange(filters.page + 1)}
                                disabled={!ordersQuery.data?.hasNextPage}
                                className="rounded-full border border-slate-200 px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:text-slate-950 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                Next
                            </button>
                        </div>
                    </div>
                </section>
            )}

            {selectedOrderId ? (
                <>
                    <div className="fixed inset-0 z-30 bg-slate-950/35" onClick={handleCloseDetails} aria-hidden="true" />
                    <OrderDetailsPanel
                        order={selectedOrder}
                        isLoading={detailsQuery.isLoading}
                        isError={detailsQuery.isError || Boolean(selectedOrderAuthorityError)}
                        error={detailsQuery.error || selectedOrderAuthorityError}
                        onClose={handleCloseDetails}
                        onRetry={() => detailsQuery.refetch()}
                        onStatusChange={(candidate) => statusMutation.mutate(candidate)}
                        isStatusUpdating={statusMutation.isPending}
                        statusUpdateError={statusMutation.error?.message || null}
                    />
                </>
            ) : null}
        </div>
    )
}

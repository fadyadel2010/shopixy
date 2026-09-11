import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { AlertCircle, Package, RefreshCw, X } from 'lucide-react'
import {
    downloadAdminOrderCheckoutAttachment,
    cancelAdminOrder,
    cancelAdminShipment,
    confirmAdminReturnReceipt,
    getAdminOrderDetails,
    getAdminOrderFulfillmentActions,
    getAdminOrderPaymentActions,
    getAdminOrders,
    runAdminOrderFulfillmentAction,
    runAdminOrderPaymentAction,
    updateAdminOrderStatus,
} from '../services/adminOrdersApi'
import { presentOrderCheckoutFields } from './orderCheckoutFieldPresentation'
import { useAuthStore } from '../store/authStore'
import { requireShipmentActionAuthority } from '../modules/shipping/shipmentActionAuthority'
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
const EXECUTABLE_FULFILLMENT_ACTION_CODES = new Set([
    'confirm',
    'pack',
    'out-for-delivery',
    'delivered',
    'complete',
    'returned',
    'failed-delivery',
    'retry-delivery',
])
const PAYMENT_STATUS_OPTIONS = [
    '',
    'Pending',
    'Authorized',
    'Collected',
    'Paid',
    'PartiallyPaid',
    'Failed',
    'Refunded',
    'PartiallyRefunded',
    'AwaitingVerification',
    'Rejected',
    'Settled',
    'CashOnDelivery',
    'Expired',
    'AmountMismatch',
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
const SORT_OPTION_VALUES = new Set(SORT_OPTIONS.map((option) => option.value))
const MAX_DELIVERY_REFERENCE_LENGTH = 200
const MAX_PROOF_URL_LENGTH = 2048
const MAX_RECIPIENT_NAME_LENGTH = 200
const MAX_LIFECYCLE_REASON_LENGTH = 1000
const PAYMENT_ACTION_CODES = new Set(['cod-delivered', 'refund'])

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

function getAffiliateAttributionWarning(order) {
    return order?.affiliateAttributionWarning ?? null
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

export function normalizeFilters(searchParams) {
    const requestedPageSize = parsePositiveInt(searchParams.get('pageSize'), 20)
    const requestedStatus = searchParams.get('status') || ''
    const requestedPaymentStatus = searchParams.get('paymentStatus') || ''
    const requestedSort = `${searchParams.get('sortBy') || 'createdAt'}:${searchParams.get('sortDirection') || 'desc'}`
    const [sortBy, sortDirection] = SORT_OPTION_VALUES.has(requestedSort)
        ? requestedSort.split(':')
        : ['createdAt', 'desc']

    return {
        page: parsePositiveInt(searchParams.get('page'), 1),
        pageSize: PAGE_SIZE_OPTIONS.includes(requestedPageSize) ? requestedPageSize : 20,
        status: ORDER_STATUS_OPTIONS.includes(requestedStatus) ? requestedStatus : '',
        paymentStatus: PAYMENT_STATUS_OPTIONS.includes(requestedPaymentStatus) ? requestedPaymentStatus : '',
        fromDate: searchParams.get('fromDate') || '',
        toDate: searchParams.get('toDate') || '',
        search: searchParams.get('search') || '',
        sortBy,
        sortDirection,
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

export function getDirectOrderStatusMutationOptions(order) {
    if (!order?.status) {
        return []
    }

    const backendTargets = new Set(
        (order.availableOrderActions || [])
            .map((action) => action?.targetStatus)
            .filter(Boolean),
    )

    return [order.status, ...ORDER_STATUS_OPTIONS.filter((status) => backendTargets.has(status) && status !== order.status)]
}

export function isFulfillmentActionExecutableFromOrdersDrawer(action) {
    return EXECUTABLE_FULFILLMENT_ACTION_CODES.has(action?.code)
}

export function isPaymentActionExecutableFromOrdersDrawer(action) {
    return PAYMENT_ACTION_CODES.has(action?.actionCode)
}

export function validateManualDeliveryEvidence(evidence) {
    const deliveryReference = evidence?.deliveryReference?.trim() || ''
    const proofOfDeliveryUrl = evidence?.proofOfDeliveryUrl?.trim() || ''
    const recipientName = evidence?.recipientName?.trim() || ''

    if (!deliveryReference) return 'Delivery reference is required.'
    if (deliveryReference.length > MAX_DELIVERY_REFERENCE_LENGTH) return 'Delivery reference is too long.'
    if (!proofOfDeliveryUrl) return 'Proof of delivery URL is required.'
    if (proofOfDeliveryUrl.length > MAX_PROOF_URL_LENGTH) return 'Proof of delivery URL is too long.'
    if (/\s/.test(proofOfDeliveryUrl)) return 'Proof of delivery URL cannot contain whitespace.'
    if (recipientName.length > MAX_RECIPIENT_NAME_LENGTH) return 'Recipient name is too long.'

    try {
        const parsed = new URL(proofOfDeliveryUrl)
        if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
            return 'Proof of delivery must be an absolute HTTP or HTTPS URL.'
        }
    } catch {
        return 'Proof of delivery must be an absolute HTTP or HTTPS URL.'
    }

    return null
}

export function validateReturnReceiptEvidence(evidence, requestedQuantity) {
    const resellableQuantity = Number(evidence?.resellableQuantity)
    const damagedQuantity = Number(evidence?.damagedQuantity)
    const maximumQuantity = Number(requestedQuantity)

    if (!Number.isInteger(resellableQuantity) || !Number.isInteger(damagedQuantity)) {
        return 'Receipt quantities must be whole numbers.'
    }
    if (resellableQuantity < 0 || damagedQuantity < 0) {
        return 'Receipt quantities cannot be negative.'
    }
    if (resellableQuantity + damagedQuantity <= 0) {
        return 'At least one received item is required.'
    }
    if (!Number.isInteger(maximumQuantity) || maximumQuantity <= 0 || resellableQuantity + damagedQuantity > maximumQuantity) {
        return 'Receipt quantities cannot exceed the approved return quantity.'
    }

    return null
}

export function getLifecycleMutationErrorMessage(error) {
    return error?.status === 409
        ? 'The order changed. Canonical data was refreshed; review it before trying again.'
        : error?.message || 'The lifecycle action could not be completed.'
}

function createActionReference(prefix) {
    const randomId = globalThis.crypto?.randomUUID?.()
    return randomId ? `${prefix}-${randomId}` : `${prefix}-${Date.now()}`
}

function isSafeTrackingUrl(value) {
    try {
        const parsed = new URL(value)
        return parsed.protocol === 'http:' || parsed.protocol === 'https:'
    } catch {
        return false
    }
}

function SectionCard({ title, children }) {
    return (
        <section className="rounded-3xl border border-slate-200 bg-white p-5 shadow-sm">
            <h3 className="text-sm font-semibold uppercase tracking-[0.18em] text-slate-500">{title}</h3>
            <div className="mt-4">{children}</div>
        </section>
    )
}

function OrderDetailsPanel({
    order,
    isLoading,
    isError,
    error,
    onClose,
    onRetry,
    isRetrying,
    onStatusChange,
    isStatusUpdating,
    statusUpdateError,
    fulfillmentAuthority,
    fulfillmentAuthorityError,
    isFulfillmentAuthorityLoading,
    onFulfillmentAction,
    isFulfillmentActionUpdating,
    fulfillmentActionError,
    paymentAuthority,
    paymentAuthorityError,
    isPaymentAuthorityLoading,
    onPaymentAction,
    isPaymentActionUpdating,
    paymentActionError,
    onAdministrativeAction,
    isAdministrativeActionUpdating,
    administrativeActionError,
    onConfirmReturnReceipt,
    isReturnReceiptUpdating,
    returnReceiptError,
}) {
    const { t } = useTranslation()
    const closeButtonRef = useRef(null)
    const [statusCandidate, setStatusCandidate] = useState(order?.status || '')
    const [statusReason, setStatusReason] = useState('')
    const [pendingFulfillmentAction, setPendingFulfillmentAction] = useState(null)
    const [fulfillmentEvidence, setFulfillmentEvidence] = useState({
        deliveryReference: '',
        proofOfDeliveryUrl: '',
        recipientName: '',
        reason: '',
        attemptNumber: '1',
        canRetry: true,
        returnAuthorizationNumber: '',
    })
    const [pendingPaymentAction, setPendingPaymentAction] = useState(null)
    const [paymentEvidence, setPaymentEvidence] = useState({
        amount: '',
        reason: '',
        deliveryReference: '',
        refundId: '',
    })
    const [pendingAdministrativeAction, setPendingAdministrativeAction] = useState(null)
    const [administrativeReason, setAdministrativeReason] = useState('')
    const [pendingReturnReceipt, setPendingReturnReceipt] = useState(null)
    const [returnReceiptEvidence, setReturnReceiptEvidence] = useState({
        resellableQuantity: '0',
        damagedQuantity: '0',
        notes: '',
    })

    const directStatusOptions = getDirectOrderStatusMutationOptions(order)
    const canSubmitStatusCandidate = Boolean(
        order?.rowVersion &&
        statusCandidate &&
        statusCandidate !== order.status &&
        directStatusOptions.includes(statusCandidate),
    )
    const fulfillmentActions = fulfillmentAuthority?.availableActions || []
    const paymentActions = paymentAuthority?.availableActions || []
    const orderActions = (order?.availableOrderActions || []).filter((action) => action?.code === 'cancel-order')
    const shipmentActions = (order?.availableShipmentActions || []).filter((action) => action?.code === 'cancel-shipment')
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

    function resetFulfillmentEvidence() {
        setPendingFulfillmentAction(null)
        setFulfillmentEvidence({
            deliveryReference: '',
            proofOfDeliveryUrl: '',
            recipientName: '',
            reason: '',
            attemptNumber: '1',
            canRetry: true,
            returnAuthorizationNumber: '',
        })
    }

    function handleFulfillmentActionSelection(action) {
        if (!isFulfillmentActionExecutableFromOrdersDrawer(action)) {
            return
        }

        if ((action.requiredFields || []).length > 0 || ['delivered', 'failed-delivery', 'returned'].includes(action.code)) {
            setPendingFulfillmentAction(action)
            return
        }

        onFulfillmentAction({ actionCode: action.code, payload: {} })
    }

    function handleSubmitFulfillmentEvidence(event) {
        event.preventDefault()
        if (!pendingFulfillmentAction) {
            return
        }

        const payload = pendingFulfillmentAction.code === 'delivered'
            ? {
                deliveryReference: fulfillmentEvidence.deliveryReference.trim(),
                proofOfDeliveryUrl: fulfillmentEvidence.proofOfDeliveryUrl.trim(),
                recipientName: fulfillmentEvidence.recipientName.trim() || null,
            }
            : pendingFulfillmentAction.code === 'failed-delivery'
                ? {
                    reason: fulfillmentEvidence.reason.trim(),
                    attemptNumber: Math.max(1, Number.parseInt(fulfillmentEvidence.attemptNumber, 10) || 1),
                    canRetry: fulfillmentEvidence.canRetry,
                }
                : {
                    reason: fulfillmentEvidence.reason.trim(),
                    shouldRefund: false,
                    returnAuthorizationNumber: fulfillmentEvidence.returnAuthorizationNumber.trim() || null,
                }

        onFulfillmentAction({ actionCode: pendingFulfillmentAction.code, payload })
    }

    const deliveryEvidenceError = pendingFulfillmentAction?.code === 'delivered'
        ? validateManualDeliveryEvidence(fulfillmentEvidence)
        : null
    const canSubmitFulfillmentEvidence = pendingFulfillmentAction?.code === 'delivered'
        ? !deliveryEvidenceError
        : Boolean(
            fulfillmentEvidence.reason.trim() &&
            fulfillmentEvidence.reason.trim().length <= MAX_LIFECYCLE_REASON_LENGTH,
        )

    function handlePaymentActionSelection(action) {
        if (!isPaymentActionExecutableFromOrdersDrawer(action)) return
        setPendingPaymentAction(action)
        setPaymentEvidence({
            amount: '',
            reason: '',
            deliveryReference: '',
            refundId: action.actionCode === 'refund' ? createActionReference('refund') : '',
        })
    }

    function handleSubmitPaymentAction(event) {
        event.preventDefault()
        if (!pendingPaymentAction) return

        const payload = pendingPaymentAction.actionCode === 'refund'
            ? {
                amount: Number(paymentEvidence.amount),
                refundId: paymentEvidence.refundId,
                reason: paymentEvidence.reason.trim() || null,
            }
            : {
                deliveryReference: paymentEvidence.deliveryReference.trim() || null,
            }
        onPaymentAction({ actionCode: pendingPaymentAction.actionCode, payload })
    }

    const canSubmitPaymentAction = pendingPaymentAction?.actionCode === 'refund'
        ? Number.isFinite(Number(paymentEvidence.amount)) && Number(paymentEvidence.amount) > 0 && Boolean(paymentEvidence.refundId)
        : Boolean(pendingPaymentAction)

    function handleSubmitAdministrativeAction(event) {
        event.preventDefault()
        if (!pendingAdministrativeAction || !administrativeReason.trim()) return
        onAdministrativeAction({
            actionCode: pendingAdministrativeAction.code,
            reason: administrativeReason.trim(),
        })
    }

    function handleReturnReceiptSelection(returnRequest) {
        setPendingReturnReceipt(returnRequest)
        setReturnReceiptEvidence({
            resellableQuantity: String(returnRequest.requestedQuantity || 0),
            damagedQuantity: '0',
            notes: '',
        })
    }

    function handleSubmitReturnReceipt(event) {
        event.preventDefault()
        if (!pendingReturnReceipt || validateReturnReceiptEvidence(returnReceiptEvidence, pendingReturnReceipt.requestedQuantity)) {
            return
        }

        onConfirmReturnReceipt({
            returnId: pendingReturnReceipt.returnId,
            payload: {
                resellableQuantity: Number(returnReceiptEvidence.resellableQuantity),
                damagedQuantity: Number(returnReceiptEvidence.damagedQuantity),
                notes: returnReceiptEvidence.notes.trim() || null,
            },
        })
    }

    const returnReceiptEvidenceError = pendingReturnReceipt
        ? validateReturnReceiptEvidence(returnReceiptEvidence, pendingReturnReceipt.requestedQuantity)
        : null

    useEffect(() => {
        setStatusCandidate(order?.status || '')
        setStatusReason('')
        setPendingFulfillmentAction(null)
        setFulfillmentEvidence({
            deliveryReference: '',
            proofOfDeliveryUrl: '',
            recipientName: '',
            reason: '',
            attemptNumber: '1',
            canRetry: true,
            returnAuthorizationNumber: '',
        })
        setPendingPaymentAction(null)
        setPaymentEvidence({ amount: '', reason: '', deliveryReference: '', refundId: '' })
        setPendingAdministrativeAction(null)
        setAdministrativeReason('')
        setPendingReturnReceipt(null)
        setReturnReceiptEvidence({ resellableQuantity: '0', damagedQuantity: '0', notes: '' })
    }, [order?.orderId, order?.status, order?.paymentStatus, order?.fulfillmentStatus, order?.shipmentStatus, order?.items])

    useEffect(() => {
        closeButtonRef.current?.focus()
        function handleKeyDown(event) {
            if (event.key === 'Escape') onClose()
        }
        document.addEventListener('keydown', handleKeyDown)
        return () => document.removeEventListener('keydown', handleKeyDown)
    }, [onClose])

    return (
        <aside
            className="fixed inset-y-0 end-0 z-40 w-full max-w-2xl border-s border-slate-200 bg-white shadow-2xl"
            role="dialog"
            aria-modal="true"
            aria-labelledby="order-details-heading"
        >
            <div className="flex h-full flex-col">
                <div className="flex items-start justify-between border-b border-slate-200 px-6 py-5">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Order details</p>
                        <h2 id="order-details-heading" className="mt-2 text-xl font-semibold text-slate-950">
                            {order?.orderNumber || 'Loading order'}
                        </h2>
                    </div>
                    <button
                        ref={closeButtonRef}
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
                        <div role="alert" className="rounded-3xl border border-rose-200 bg-rose-50 p-5 text-sm text-rose-700">
                            <p className="font-semibold">Unable to load the selected order.</p>
                            <p className="mt-2">{error?.message || 'Please try again.'}</p>
                            <button
                                type="button"
                                onClick={onRetry}
                                disabled={isRetrying}
                                className="mt-4 inline-flex items-center gap-2 rounded-full border border-rose-200 bg-white px-4 py-2 text-sm font-semibold text-rose-700 transition hover:border-rose-300 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                <RefreshCw className={`h-4 w-4 ${isRetrying ? 'animate-spin' : ''}`} />
                                Retry
                            </button>
                        </div>
                    ) : null}

                    {!isLoading && !isError && order ? (
                        <div className="space-y-5">
                            {!order.isMarketplaceOrder ? <SectionCard title="Lifecycle">
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
                                                {directStatusOptions.map((option) => (
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
                                    <p className="mt-3 text-sm text-slate-600">
                                        Shipment, delivery, completion, cancellation, and payment-driven states are controlled by dedicated lifecycle actions.
                                    </p>
                                </form>

                                <div className="mt-5 rounded-2xl border border-slate-200 bg-white p-4">
                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                        Fulfillment lifecycle
                                    </p>
                                    <div className="mt-3 grid gap-4 md:grid-cols-2">
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Fulfillment status</p>
                                            <p className="mt-2 text-sm font-medium text-slate-900">{order.fulfillmentStatus || fulfillmentAuthority?.currentStatus || 'Unavailable'}</p>
                                        </div>
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Authority</p>
                                            <p className="mt-2 text-sm font-medium text-slate-900">
                                                {isFulfillmentAuthorityLoading ? 'Loading actions' : `${fulfillmentActions.length} available`}
                                            </p>
                                        </div>
                                    </div>
                                    {fulfillmentAuthorityError ? (
                                        <p role="alert" className="mt-3 text-sm text-rose-700">{fulfillmentAuthorityError}</p>
                                    ) : null}
                                    {fulfillmentActionError ? (
                                        <p role="alert" className="mt-3 text-sm text-rose-700">{fulfillmentActionError}</p>
                                    ) : null}
                                    <div className="mt-4 flex flex-wrap gap-2">
                                        {fulfillmentActions.length ? fulfillmentActions.map((action) => {
                                            const executable = isFulfillmentActionExecutableFromOrdersDrawer(action)
                                            return (
                                                <button
                                                    key={action.code}
                                                    type="button"
                                                    onClick={() => handleFulfillmentActionSelection(action)}
                                                    disabled={!executable || isFulfillmentActionUpdating}
                                                    aria-pressed={pendingFulfillmentAction?.code === action.code}
                                                    title={executable ? action.label : 'This action is not supported by the current admin control.'}
                                                    className={`rounded-full border px-4 py-2 text-sm font-semibold transition disabled:cursor-not-allowed disabled:opacity-50 ${pendingFulfillmentAction?.code === action.code ? 'border-sky-300 bg-sky-50 text-sky-800' : 'border-slate-200 text-slate-700 hover:border-slate-300 hover:text-slate-950'}`}
                                                >
                                                    {isFulfillmentActionUpdating ? 'Working' : action.label}
                                                </button>
                                            )
                                        }) : (
                                            <p className="text-sm text-slate-500">
                                                No direct fulfillment actions are currently available for this order.
                                            </p>
                                        )}
                                    </div>

                                    {pendingFulfillmentAction ? (
                                        <form className="mt-4 rounded-2xl border border-sky-200 bg-sky-50 p-4" onSubmit={handleSubmitFulfillmentEvidence}>
                                            <p className="text-sm font-semibold text-sky-950">{pendingFulfillmentAction.label}</p>
                                            <p className="mt-1 text-xs text-sky-800">
                                                Submit the evidence required by the backend lifecycle contract.
                                            </p>

                                            {pendingFulfillmentAction.code === 'delivered' ? (
                                                <div className="mt-4 grid gap-3 md:grid-cols-2">
                                                    <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                        <span className="font-medium">Delivery reference</span>
                                                        <input autoFocus required maxLength={MAX_DELIVERY_REFERENCE_LENGTH} value={fulfillmentEvidence.deliveryReference} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, deliveryReference: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-sky-400 focus:ring-2 focus:ring-sky-100" />
                                                    </label>
                                                    <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                        <span className="font-medium">Proof of delivery URL</span>
                                                        <input required type="url" inputMode="url" maxLength={MAX_PROOF_URL_LENGTH} aria-describedby="delivery-proof-error" value={fulfillmentEvidence.proofOfDeliveryUrl} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, proofOfDeliveryUrl: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-sky-400 focus:ring-2 focus:ring-sky-100" />
                                                    </label>
                                                    <label className="flex flex-col gap-2 text-sm text-slate-700 md:col-span-2">
                                                        <span className="font-medium">Recipient name (optional)</span>
                                                        <input maxLength={MAX_RECIPIENT_NAME_LENGTH} value={fulfillmentEvidence.recipientName} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, recipientName: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-sky-400 focus:ring-2 focus:ring-sky-100" />
                                                    </label>
                                                    {deliveryEvidenceError ? (
                                                        <p id="delivery-proof-error" role="alert" className="text-sm text-rose-700 md:col-span-2">{deliveryEvidenceError}</p>
                                                    ) : null}
                                                </div>
                                            ) : (
                                                <div className="mt-4 grid gap-3 md:grid-cols-2">
                                                    <label className="flex flex-col gap-2 text-sm text-slate-700 md:col-span-2">
                                                        <span className="font-medium">Reason</span>
                                                        <textarea autoFocus required maxLength={MAX_LIFECYCLE_REASON_LENGTH} value={fulfillmentEvidence.reason} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, reason: event.target.value }))} className="min-h-24 rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-sky-400 focus:ring-2 focus:ring-sky-100" />
                                                    </label>
                                                    {pendingFulfillmentAction.code === 'failed-delivery' ? (
                                                        <>
                                                            <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                                <span className="font-medium">Attempt number</span>
                                                                <input type="number" min="1" value={fulfillmentEvidence.attemptNumber} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, attemptNumber: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-sky-400 focus:ring-2 focus:ring-sky-100" />
                                                            </label>
                                                            <label className="flex items-center gap-2 self-end rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-700">
                                                                <input type="checkbox" checked={fulfillmentEvidence.canRetry} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, canRetry: event.target.checked }))} />
                                                                Delivery can be retried
                                                            </label>
                                                        </>
                                                    ) : (
                                                        <label className="flex flex-col gap-2 text-sm text-slate-700 md:col-span-2">
                                                            <span className="font-medium">Return authorization (optional)</span>
                                                            <input value={fulfillmentEvidence.returnAuthorizationNumber} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, returnAuthorizationNumber: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-sky-400 focus:ring-2 focus:ring-sky-100" />
                                                        </label>
                                                    )}
                                                </div>
                                            )}

                                            <div className="mt-4 flex flex-wrap gap-2">
                                                <button type="submit" disabled={!canSubmitFulfillmentEvidence || isFulfillmentActionUpdating} className="rounded-full bg-slate-950 px-4 py-2 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-300">
                                                    {isFulfillmentActionUpdating ? 'Submitting' : `Confirm ${pendingFulfillmentAction.label}`}
                                                </button>
                                                <button type="button" onClick={resetFulfillmentEvidence} disabled={isFulfillmentActionUpdating} className="rounded-full border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-700 disabled:cursor-not-allowed disabled:opacity-50">
                                                    Cancel
                                                </button>
                                            </div>
                                        </form>
                                    ) : null}
                                </div>
                            </SectionCard> : <section className="rounded-3xl border border-blue-200 bg-blue-50 p-5">
                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-blue-700">CommerceHub vendor sub-order</p>
                                <h3 className="mt-2 text-lg font-semibold text-slate-950">Marketplace fulfillment is isolated from Storefront order authority.</h3>
                                <p className="mt-2 text-sm leading-6 text-slate-600">Use the dedicated CommerceHub surface for backend-authorized vendor fulfillment, return receipt, and marketplace lifecycle history.</p>
                                <Link to={`/commercehub/orders/${order.orderId}`} className="mt-4 inline-flex rounded-full bg-blue-700 px-4 py-2 text-sm font-semibold text-white">Open CommerceHub order</Link>
                            </section>}

                            {!order.isMarketplaceOrder ? (
                                <SectionCard title="Payment lifecycle">
                                    <div className="grid gap-4 md:grid-cols-2">
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Canonical payment status</p>
                                            <div className="mt-2"><StatusBadge value={order.paymentStatus} tone={resolvePaymentTone(order.paymentStatus)} /></div>
                                        </div>
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Payment method</p>
                                            <p className="mt-2 text-sm font-medium text-slate-900">{paymentAuthority?.paymentMethodCode || 'Unavailable'}</p>
                                        </div>
                                    </div>
                                    {paymentAuthority?.statusMessage ? <p className="mt-3 text-sm text-slate-600">{paymentAuthority.statusMessage}</p> : null}
                                    {paymentAuthorityError ? <p role="alert" className="mt-3 text-sm text-rose-700">{paymentAuthorityError}</p> : null}
                                    {paymentActionError ? <p role="alert" className="mt-3 text-sm text-rose-700">{paymentActionError}</p> : null}
                                    <div className="mt-4 flex flex-wrap gap-2">
                                        {isPaymentAuthorityLoading ? <p role="status" className="text-sm text-slate-500">Loading payment actions…</p> : null}
                                        {!isPaymentAuthorityLoading && paymentActions.length ? paymentActions.map((action) => {
                                            const executable = isPaymentActionExecutableFromOrdersDrawer(action)
                                            return (
                                                <button
                                                    key={action.actionCode}
                                                    type="button"
                                                    onClick={() => handlePaymentActionSelection(action)}
                                                    disabled={!executable || isPaymentActionUpdating}
                                                    aria-pressed={pendingPaymentAction?.actionCode === action.actionCode}
                                                    title={action.description}
                                                    className="rounded-full border border-slate-200 px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 disabled:cursor-not-allowed disabled:opacity-50"
                                                >
                                                    {isPaymentActionUpdating ? 'Working' : action.label}
                                                </button>
                                            )
                                        }) : null}
                                        {!isPaymentAuthorityLoading && !paymentActions.length ? <p className="text-sm text-slate-500">No payment actions are currently available.</p> : null}
                                    </div>

                                    {pendingPaymentAction ? (
                                        <form className="mt-4 rounded-2xl border border-amber-200 bg-amber-50 p-4" onSubmit={handleSubmitPaymentAction}>
                                            <p className="text-sm font-semibold text-amber-950">{pendingPaymentAction.label}</p>
                                            <p className="mt-1 text-xs text-amber-800">{pendingPaymentAction.description}</p>
                                            {pendingPaymentAction.actionCode === 'refund' ? (
                                                <div className="mt-4 grid gap-3 md:grid-cols-2">
                                                    <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                        <span className="font-medium">Refund amount</span>
                                                        <input autoFocus required type="number" min="0.01" step="0.01" value={paymentEvidence.amount} onChange={(event) => setPaymentEvidence((current) => ({ ...current, amount: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                                    </label>
                                                    <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                        <span className="font-medium">Refund reason (optional)</span>
                                                        <input maxLength={MAX_LIFECYCLE_REASON_LENGTH} value={paymentEvidence.reason} onChange={(event) => setPaymentEvidence((current) => ({ ...current, reason: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                                    </label>
                                                    <p className="text-xs text-amber-800 md:col-span-2">A refund changes payment state only; it does not relabel the order or physical return.</p>
                                                </div>
                                            ) : (
                                                <label className="mt-4 flex flex-col gap-2 text-sm text-slate-700">
                                                    <span className="font-medium">Collection reference (optional)</span>
                                                    <input autoFocus maxLength={MAX_DELIVERY_REFERENCE_LENGTH} value={paymentEvidence.deliveryReference} onChange={(event) => setPaymentEvidence((current) => ({ ...current, deliveryReference: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                                </label>
                                            )}
                                            <div className="mt-4 flex flex-wrap gap-2">
                                                <button type="submit" disabled={!canSubmitPaymentAction || isPaymentActionUpdating} className="rounded-full bg-slate-950 px-4 py-2 text-sm font-semibold text-white disabled:bg-slate-300">{isPaymentActionUpdating ? 'Submitting' : `Confirm ${pendingPaymentAction.label}`}</button>
                                                <button type="button" disabled={isPaymentActionUpdating} onClick={() => setPendingPaymentAction(null)} className="rounded-full border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-700 disabled:opacity-50">Cancel</button>
                                            </div>
                                        </form>
                                    ) : null}
                                </SectionCard>
                            ) : null}

                            <SectionCard title="Shipment evidence">
                                {order.shipment ? (
                                    <div className="grid gap-4 md:grid-cols-2">
                                        <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">Shipment status</p><p className="mt-2 text-sm font-medium text-slate-900">{order.shipment.status}</p></div>
                                        <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">Carrier</p><p className="mt-2 text-sm font-medium text-slate-900">{order.shipment.carrierName || order.shipment.shippingMethodName || 'Manual / unavailable'}</p></div>
                                        <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">Shipment number</p><p className="mt-2 break-all text-sm font-medium text-slate-900">{order.shipment.shipmentNumber}</p></div>
                                        <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">Tracking number</p><p className="mt-2 break-all text-sm font-medium text-slate-900">{order.shipment.trackingNumber || 'Unavailable'}</p></div>
                                        {isSafeTrackingUrl(order.shipment.trackingUrl) ? <a className="text-sm font-semibold text-sky-700 underline md:col-span-2" href={order.shipment.trackingUrl} target="_blank" rel="noreferrer">Open carrier tracking</a> : null}
                                    </div>
                                ) : <p className="text-sm text-slate-500">No shipment exists for this order.</p>}
                            </SectionCard>

                            {(orderActions.length || shipmentActions.length) ? (
                                <SectionCard title="Administrative lifecycle commands">
                                    {administrativeActionError ? <p role="alert" className="mb-3 text-sm text-rose-700">{administrativeActionError}</p> : null}
                                    <div className="flex flex-wrap gap-2">
                                        {[...orderActions, ...shipmentActions].map((action) => (
                                            <button key={action.code} type="button" disabled={isAdministrativeActionUpdating} onClick={() => { setPendingAdministrativeAction(action); setAdministrativeReason('') }} className="rounded-full border border-rose-200 px-4 py-2 text-sm font-semibold text-rose-700 disabled:opacity-50">{action.label}</button>
                                        ))}
                                    </div>
                                    {pendingAdministrativeAction ? (
                                        <form className="mt-4 rounded-2xl border border-rose-200 bg-rose-50 p-4" onSubmit={handleSubmitAdministrativeAction}>
                                            <p className="text-sm font-semibold text-rose-950">Confirm {pendingAdministrativeAction.label}</p>
                                            <p className="mt-1 text-xs text-rose-800">This command is separate from payment refund and other lifecycle actions.</p>
                                            <label className="mt-4 flex flex-col gap-2 text-sm text-slate-700">
                                                <span className="font-medium">Reason</span>
                                                <textarea autoFocus required maxLength={MAX_LIFECYCLE_REASON_LENGTH} value={administrativeReason} onChange={(event) => setAdministrativeReason(event.target.value)} className="min-h-24 rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                            </label>
                                            <div className="mt-4 flex gap-2">
                                                <button type="submit" disabled={!administrativeReason.trim() || isAdministrativeActionUpdating} className="rounded-full bg-rose-700 px-4 py-2 text-sm font-semibold text-white disabled:bg-slate-300">{isAdministrativeActionUpdating ? 'Submitting' : `Confirm ${pendingAdministrativeAction.label}`}</button>
                                                <button type="button" disabled={isAdministrativeActionUpdating} onClick={() => setPendingAdministrativeAction(null)} className="rounded-full border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-700">Keep current state</button>
                                            </div>
                                        </form>
                                    ) : null}
                                </SectionCard>
                            ) : null}

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

                            {getAffiliateAttributionWarning(order) ? (
                                <section role="status" className="rounded-3xl border border-amber-200 bg-amber-50 p-5 text-sm text-amber-800">
                                    Affiliate attribution is unavailable for this order. Order details remain available.
                                </section>
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
                                                {item.refunds?.length ? (
                                                    <div className="mt-4 space-y-3 border-t border-slate-200 pt-4">
                                                        {item.refunds.map((refund) => {
                                                            const returnRequest = refund.returnRequest
                                                            const isReceiptOpen = pendingReturnReceipt?.returnId === returnRequest?.returnId

                                                            return (
                                                                <div key={refund.refundId} className="rounded-2xl border border-amber-200 bg-amber-50 p-4">
                                                                    <div className="flex flex-wrap items-start justify-between gap-3">
                                                                        <div>
                                                                            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-amber-800">Customer refund</p>
                                                                            <p className="mt-2 text-sm font-medium text-slate-950">{refund.reason || 'No reason provided'}</p>
                                                                            <p className="mt-1 text-xs text-slate-600">Requested {formatDateTime(refund.createdAt)}</p>
                                                                        </div>
                                                                        <div className="text-right">
                                                                            <StatusBadge value={refund.status} tone="amber" />
                                                                            <p className="mt-2 text-sm font-semibold text-slate-950">
                                                                                {formatCurrency(refund.amount, order.totals?.currency)} | Qty {refund.quantity}
                                                                            </p>
                                                                        </div>
                                                                    </div>

                                                                    {returnRequest ? (
                                                                        <div className="mt-4 rounded-2xl border border-slate-200 bg-white p-4">
                                                                            <div className="grid gap-3 md:grid-cols-2">
                                                                                <div>
                                                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Physical return</p>
                                                                                    <p className="mt-2 text-sm font-semibold text-slate-950">{returnRequest.status}</p>
                                                                                </div>
                                                                                <div>
                                                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Approved quantity</p>
                                                                                    <p className="mt-2 text-sm font-semibold text-slate-950">{returnRequest.requestedQuantity}</p>
                                                                                </div>
                                                                                {returnRequest.courierName || returnRequest.trackingNumber ? (
                                                                                    <div className="md:col-span-2">
                                                                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Return tracking</p>
                                                                                        <p className="mt-2 text-sm text-slate-700">
                                                                                            {[returnRequest.courierName, returnRequest.trackingNumber].filter(Boolean).join(' | ')}
                                                                                        </p>
                                                                                    </div>
                                                                                ) : null}
                                                                                {returnRequest.status === 'Confirmed' ? (
                                                                                    <div className="md:col-span-2">
                                                                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">Received condition</p>
                                                                                        <p className="mt-2 text-sm text-slate-700">
                                                                                            Resellable {returnRequest.receivedResellableQuantity} | Damaged {returnRequest.receivedDamagedQuantity}
                                                                                        </p>
                                                                                    </div>
                                                                                ) : null}
                                                                            </div>

                                                                            {returnRequest.canConfirmReceipt ? (
                                                                                <button
                                                                                    type="button"
                                                                                    onClick={() => handleReturnReceiptSelection(returnRequest)}
                                                                                    disabled={isReturnReceiptUpdating}
                                                                                    className="mt-4 rounded-full bg-slate-950 px-4 py-2 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-300"
                                                                                >
                                                                                    Confirm received condition
                                                                                </button>
                                                                            ) : returnRequest.status !== 'Confirmed' ? (
                                                                                <p className="mt-4 text-sm text-slate-600">
                                                                                    Receipt confirmation becomes available after platform logistics delivers the return to this vendor.
                                                                                </p>
                                                                            ) : null}

                                                                            {isReceiptOpen ? (
                                                                                <form className="mt-4 rounded-2xl border border-sky-200 bg-sky-50 p-4" onSubmit={handleSubmitReturnReceipt}>
                                                                                    <p className="text-sm font-semibold text-sky-950">Confirm received inventory condition</p>
                                                                                    <p className="mt-1 text-xs text-sky-800">This records vendor receipt only. It does not execute the customer refund.</p>
                                                                                    <div className="mt-4 grid gap-3 md:grid-cols-2">
                                                                                        <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                                                            <span className="font-medium">Resellable quantity</span>
                                                                                            <input type="number" min="0" step="1" value={returnReceiptEvidence.resellableQuantity} onChange={(event) => setReturnReceiptEvidence((current) => ({ ...current, resellableQuantity: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                                                                        </label>
                                                                                        <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                                                            <span className="font-medium">Damaged quantity</span>
                                                                                            <input type="number" min="0" step="1" value={returnReceiptEvidence.damagedQuantity} onChange={(event) => setReturnReceiptEvidence((current) => ({ ...current, damagedQuantity: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                                                                        </label>
                                                                                        <label className="flex flex-col gap-2 text-sm text-slate-700 md:col-span-2">
                                                                                            <span className="font-medium">Receipt notes (optional)</span>
                                                                                            <textarea maxLength={MAX_LIFECYCLE_REASON_LENGTH} value={returnReceiptEvidence.notes} onChange={(event) => setReturnReceiptEvidence((current) => ({ ...current, notes: event.target.value }))} className="min-h-20 rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                                                                        </label>
                                                                                    </div>
                                                                                    {returnReceiptEvidenceError ? <p role="alert" className="mt-3 text-sm text-rose-700">{returnReceiptEvidenceError}</p> : null}
                                                                                    {returnReceiptError ? <p role="alert" className="mt-3 text-sm text-rose-700">{returnReceiptError}</p> : null}
                                                                                    <div className="mt-4 flex flex-wrap gap-2">
                                                                                        <button type="submit" disabled={Boolean(returnReceiptEvidenceError) || isReturnReceiptUpdating} className="rounded-full bg-slate-950 px-4 py-2 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-300">
                                                                                            {isReturnReceiptUpdating ? 'Confirming receipt' : 'Confirm receipt'}
                                                                                        </button>
                                                                                        <button type="button" onClick={() => setPendingReturnReceipt(null)} disabled={isReturnReceiptUpdating} className="rounded-full border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-700 disabled:opacity-50">
                                                                                            Cancel
                                                                                        </button>
                                                                                    </div>
                                                                                </form>
                                                                            ) : null}
                                                                        </div>
                                                                    ) : null}
                                                                </div>
                                                            )
                                                        })}
                                                    </div>
                                                ) : null}
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
        queryFn: ({ signal }) => getAdminOrders(filters, signal),
        staleTime: 30_000,
    })

    const detailsQuery = useQuery({
        queryKey: [...authorityScope, 'details', selectedOrderId],
        queryFn: ({ signal }) => getAdminOrderDetails(selectedOrderId, signal),
        enabled: Boolean(selectedOrderId),
        staleTime: 30_000,
    })

    const fulfillmentAuthorityQuery = useQuery({
        queryKey: [...authorityScope, 'fulfillment-actions', selectedOrderId],
        queryFn: async ({ signal }) => {
            const authority = await getAdminOrderFulfillmentActions(selectedOrderId, signal)
            return requireShipmentActionAuthority(authority, selectedOrderId)
        },
        enabled: Boolean(selectedOrderId && detailsQuery.data && detailsQuery.data.isMarketplaceOrder !== true),
        staleTime: 15_000,
    })

    const paymentAuthorityQuery = useQuery({
        queryKey: [...authorityScope, 'payment-actions', selectedOrderId],
        queryFn: ({ signal }) => getAdminOrderPaymentActions(selectedOrderId, signal),
        enabled: Boolean(selectedOrderId && detailsQuery.data && detailsQuery.data.isMarketplaceOrder !== true),
        staleTime: 15_000,
    })

    let admittedOrderList = null
    let orderListAuthorityError = null
    if (ordersQuery.data) {
        try {
            admittedOrderList = requireCurrentVendorOrderVisibilityListAuthority(ordersQuery.data, user)
        } catch (error) {
            orderListAuthorityError = error
        }
    }

    const orders = admittedOrderList?.items || []
    const totalCount = admittedOrderList?.totalCount || 0
    const totalPages = admittedOrderList?.totalPages || 0
    const currentPage = admittedOrderList?.page || filters.page
    const orderListError = ordersQuery.error || orderListAuthorityError

    let selectedOrder = null
    let selectedOrderAuthorityError = null
    if (detailsQuery.data && selectedOrderId) {
        try {
            selectedOrder = requireCurrentVendorOrderVisibilityAuthority(detailsQuery.data, user, selectedOrderId)
        } catch (error) {
            selectedOrderAuthorityError = error
        }
    }

    async function refreshCanonicalState() {
        await Promise.all([
            queryClient.invalidateQueries({ queryKey: [...authorityScope, 'list'] }),
            queryClient.invalidateQueries({ queryKey: [...authorityScope, 'details', selectedOrderId] }),
            queryClient.invalidateQueries({ queryKey: [...authorityScope, 'fulfillment-actions', selectedOrderId] }),
            queryClient.invalidateQueries({ queryKey: [...authorityScope, 'payment-actions', selectedOrderId] }),
        ])
    }

    function refreshAfterConflict(error) {
        if (error?.status === 409) {
            void refreshCanonicalState()
        }
    }

    const statusMutation = useMutation({
        mutationFn: (candidate) => updateAdminOrderStatus(selectedOrderId, candidate),
        onSuccess: refreshCanonicalState,
        onError: refreshAfterConflict,
    })

    const fulfillmentActionMutation = useMutation({
        mutationFn: ({ actionCode, payload }) => runAdminOrderFulfillmentAction(selectedOrderId, actionCode, payload),
        onSuccess: refreshCanonicalState,
        onError: refreshAfterConflict,
    })

    const paymentActionMutation = useMutation({
        mutationFn: ({ actionCode, payload }) => runAdminOrderPaymentAction(selectedOrderId, actionCode, payload),
        onSuccess: refreshCanonicalState,
        onError: refreshAfterConflict,
    })

    const administrativeActionMutation = useMutation({
        mutationFn: ({ actionCode, reason }) => {
            if (actionCode === 'cancel-order') return cancelAdminOrder(selectedOrderId, reason)
            if (actionCode === 'cancel-shipment' && selectedOrder?.shipment?.shipmentId) {
                return cancelAdminShipment(selectedOrder.shipment.shipmentId, reason)
            }
            throw new Error('The backend administrative action could not be matched to this order.')
        },
        onSuccess: refreshCanonicalState,
        onError: refreshAfterConflict,
    })

    const returnReceiptMutation = useMutation({
        mutationFn: ({ returnId, payload }) => confirmAdminReturnReceipt(returnId, payload),
        onSuccess: refreshCanonicalState,
        onError: refreshAfterConflict,
    })

    useEffect(() => {
        const responsePage = ordersQuery.data?.page
        if (!Number.isInteger(responsePage) || responsePage < 1 || responsePage === filters.page) {
            return
        }

        setSearchParams((previous) => {
            const next = new URLSearchParams(previous)
            next.set('page', String(responsePage))
            return next
        }, { replace: true })
    }, [filters.page, ordersQuery.data?.page, setSearchParams])

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
                            Tenant-scoped Storefront order authority. CommerceHub rows remain visible for compatibility and open in their dedicated vendor operations surface.
                        </p>
                    </div>
                    <button
                        type="button"
                        onClick={() => ordersQuery.refetch()}
                        disabled={ordersQuery.isFetching}
                        className="inline-flex items-center justify-center gap-2 rounded-full border border-slate-200 bg-white px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:text-slate-950 disabled:cursor-not-allowed disabled:opacity-60"
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
                <section role="alert" className="rounded-[32px] border border-rose-200 bg-rose-50 px-6 py-8 shadow-sm">
                    <div className="flex items-start gap-3">
                        <AlertCircle className="mt-0.5 h-5 w-5 text-rose-600" />
                        <div>
                            <h2 className="text-lg font-semibold text-rose-900">Unable to load orders</h2>
                            <p className="mt-2 text-sm text-rose-700">
                                {orderListError?.message || 'Please retry after the latest session and tenant state are restored.'}
                            </p>
                            {orderListError?.code || orderListError?.traceId ? (
                                <p className="mt-2 text-xs font-medium text-rose-700">
                                    {orderListError.code ? `Code: ${orderListError.code}` : null}
                                    {orderListError.code && orderListError.traceId ? ' | ' : null}
                                    {orderListError.traceId ? `Trace: ${orderListError.traceId}` : null}
                                </p>
                            ) : null}
                            <button
                                type="button"
                                onClick={() => ordersQuery.refetch()}
                                disabled={ordersQuery.isFetching}
                                className="mt-4 inline-flex items-center gap-2 rounded-full border border-rose-200 bg-white px-4 py-2 text-sm font-semibold text-rose-700 transition hover:border-rose-300 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                <RefreshCw className={`h-4 w-4 ${ordersQuery.isFetching ? 'animate-spin' : ''}`} />
                                Retry
                            </button>
                        </div>
                    </div>
                </section>
            ) : (
                <section className="rounded-[32px] border border-slate-200 bg-white shadow-sm">
                    <div className="flex flex-col gap-3 border-b border-slate-200 px-6 py-5 sm:flex-row sm:items-center sm:justify-between">
                        <div>
                            <p className="text-sm font-semibold text-slate-950">{totalCount.toLocaleString()} orders</p>
                            <p className="mt-1 text-sm text-slate-500">
                                SQL-backed Storefront orders with read-only CommerceHub compatibility links.
                            </p>
                            {ordersQuery.isFetching && !ordersQuery.isLoading ? (
                                <p className="mt-1 text-xs font-medium text-sky-700" role="status">Refreshing orders…</p>
                            ) : null}
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
                        <table className="min-w-full divide-y divide-slate-200" aria-busy={ordersQuery.isFetching}>
                            <caption className="sr-only">Tenant-scoped orders returned by the admin Orders API</caption>
                            <thead className="bg-slate-50">
                                <tr className="text-start text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                    <th scope="col" className="px-6 py-4">Order</th>
                                    <th scope="col" className="px-6 py-4">Customer</th>
                                    <th scope="col" className="px-6 py-4">Source</th>
                                    <th scope="col" className="px-6 py-4">Total</th>
                                    <th scope="col" className="px-6 py-4">Order status</th>
                                    <th scope="col" className="px-6 py-4">Payment</th>
                                    <th scope="col" className="px-6 py-4">Created</th>
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

                                {ordersQuery.isSuccess && !ordersQuery.isFetching && !orders.length ? (
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
                                        const affiliateAttributionWarning = getAffiliateAttributionWarning(order)
                                        return (
                                        <tr
                                            key={order.orderId}
                                            className="transition hover:bg-sky-50/60"
                                        >
                                            <td className="px-6 py-5">
                                                <button
                                                    type="button"
                                                    onClick={() => order.isMarketplaceOrder
                                                        ? navigate(`/commercehub/orders/${order.orderId}`)
                                                        : handleOpenOrder(order.orderId)}
                                                    className="font-medium text-sky-700 underline-offset-4 hover:text-sky-900 hover:underline focus-visible:rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-400"
                                                    aria-label={`Open order ${order.orderNumber}`}
                                                >
                                                    {order.orderNumber}
                                                </button>
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
                                                    {affiliateAttributionWarning ? (
                                                        <StatusBadge
                                                            value="Affiliate attribution unavailable"
                                                            tone="amber"
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
                            Page {currentPage} of {Math.max(totalPages, 1)}
                        </p>
                        <div className="flex items-center gap-2">
                            <button
                                type="button"
                                onClick={() => handlePageChange(Math.max(1, currentPage - 1))}
                                disabled={currentPage <= 1 || ordersQuery.isFetching}
                                className="rounded-full border border-slate-200 px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:text-slate-950 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                Previous
                            </button>
                            <button
                                type="button"
                                onClick={() => handlePageChange(currentPage + 1)}
                                disabled={!admittedOrderList?.hasNextPage || ordersQuery.isFetching}
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
                        onRetry={() => {
                            detailsQuery.refetch()
                            fulfillmentAuthorityQuery.refetch()
                            paymentAuthorityQuery.refetch()
                        }}
                        isRetrying={detailsQuery.isFetching || fulfillmentAuthorityQuery.isFetching || paymentAuthorityQuery.isFetching}
                        onStatusChange={(candidate) => statusMutation.mutate(candidate)}
                        isStatusUpdating={statusMutation.isPending}
                        statusUpdateError={statusMutation.error ? getLifecycleMutationErrorMessage(statusMutation.error) : null}
                        fulfillmentAuthority={fulfillmentAuthorityQuery.data}
                        fulfillmentAuthorityError={fulfillmentAuthorityQuery.error?.message || null}
                        isFulfillmentAuthorityLoading={fulfillmentAuthorityQuery.isLoading}
                        onFulfillmentAction={(actionCode) => fulfillmentActionMutation.mutate(actionCode)}
                        isFulfillmentActionUpdating={fulfillmentActionMutation.isPending}
                        fulfillmentActionError={fulfillmentActionMutation.error ? getLifecycleMutationErrorMessage(fulfillmentActionMutation.error) : null}
                        paymentAuthority={paymentAuthorityQuery.data}
                        paymentAuthorityError={paymentAuthorityQuery.error?.message || null}
                        isPaymentAuthorityLoading={paymentAuthorityQuery.isLoading}
                        onPaymentAction={(action) => paymentActionMutation.mutate(action)}
                        isPaymentActionUpdating={paymentActionMutation.isPending}
                        paymentActionError={paymentActionMutation.error ? getLifecycleMutationErrorMessage(paymentActionMutation.error) : null}
                        onAdministrativeAction={(action) => administrativeActionMutation.mutate(action)}
                        isAdministrativeActionUpdating={administrativeActionMutation.isPending}
                        administrativeActionError={administrativeActionMutation.error ? getLifecycleMutationErrorMessage(administrativeActionMutation.error) : null}
                        onConfirmReturnReceipt={(request) => returnReceiptMutation.mutate(request)}
                        isReturnReceiptUpdating={returnReceiptMutation.isPending}
                        returnReceiptError={returnReceiptMutation.error ? getLifecycleMutationErrorMessage(returnReceiptMutation.error) : null}
                    />
                </>
            ) : null}
        </div>
    )
}

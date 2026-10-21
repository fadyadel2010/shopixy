import { useEffect, useMemo, useRef, useState } from 'react'
import CancellationOutcome from '../modules/shipping/components/CancellationOutcome'
import { containsAsciiControl } from '../security/textCharacterPolicy'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { AlertCircle, Ban, Flag, Package, RefreshCw, ShieldCheck, X } from 'lucide-react'
import {
    cancelAdminOrder,
    cancelAdminShipment,
    confirmAdminReturnReceipt,
    getAdminOrderCustomerTrust,
    getAdminOrderDetails,
    getAdminOrderFulfillmentActions,
    getAdminOrderPaymentActions,
    getAdminOrders,
    getAdminOrderSessionIntelligence,
    runAdminOrderCustomerPhoneAction,
    runAdminOrderFulfillmentAction,
    runAdminOrderPaymentAction,
    updateAdminOrderStatus,
} from '../services/adminOrdersApi'
import { presentOrderCheckoutFields } from './orderCheckoutFieldPresentation'
import OrderCheckoutAttachmentPreview from './OrderCheckoutAttachmentPreview'
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
    'SyntheticQaCollected',
]
const SORT_OPTIONS = [
    { value: 'createdAt:desc', labelKey: 'orders.management.sort.newest' },
    { value: 'createdAt:asc', labelKey: 'orders.management.sort.oldest' },
    { value: 'orderNumber:asc', labelKey: 'orders.management.sort.orderAsc' },
    { value: 'orderNumber:desc', labelKey: 'orders.management.sort.orderDesc' },
    { value: 'customerName:asc', labelKey: 'orders.management.sort.customerAsc' },
    { value: 'totalAmount:desc', labelKey: 'orders.management.sort.totalDesc' },
    { value: 'totalAmount:asc', labelKey: 'orders.management.sort.totalAsc' },
    { value: 'status:asc', labelKey: 'orders.management.sort.statusAsc' },
    { value: 'paymentStatus:asc', labelKey: 'orders.management.sort.paymentAsc' },
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

function formatCurrency(amount, currency, locale = 'en-US') {
    return new Intl.NumberFormat(locale, {
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

function formatDateTime(value, locale = 'en-US', unavailable = 'Unavailable') {
    if (!value) {
        return unavailable
    }

    const parsed = new Date(value)
    if (Number.isNaN(parsed.getTime())) {
        return unavailable
    }

    return parsed.toLocaleString(locale)
}

function formatJourneyDuration(seconds, t) {
    const safeSeconds = Math.max(0, Number(seconds) || 0)
    const minutes = Math.floor(safeSeconds / 60)
    const remainingSeconds = safeSeconds % 60

    return minutes > 0
        ? t('orders.sessionJourney.durationMinutes', { minutes, seconds: remainingSeconds })
        : t('orders.sessionJourney.durationSeconds', { seconds: remainingSeconds })
}

function customerRiskPresentation(t, level) {
    const presentations = {
        UNAVAILABLE: { label: t('orders.customerRisk.unavailable'), tone: 'slate' },
        CRITICAL_RISK: { label: t('orders.customerRisk.criticalRisk'), tone: 'rose' },
        HIGH_RISK: { label: t('orders.customerRisk.highRisk'), tone: 'rose' },
        SUSPICIOUS: { label: t('orders.customerRisk.suspicious'), tone: 'amber' },
        SAFE: { label: t('orders.customerRisk.safe'), tone: 'emerald' },
    }
    return presentations[level] || presentations.UNAVAILABLE
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

function StatusBadge({ value, label, tone = 'slate' }) {
    const palette = {
        slate: 'border-slate-200 bg-slate-100 text-slate-700',
        emerald: 'border-emerald-200 bg-emerald-100 text-emerald-700',
        amber: 'border-amber-200 bg-amber-100 text-amber-700',
        rose: 'border-rose-200 bg-rose-100 text-rose-700',
        sky: 'border-sky-200 bg-sky-100 text-sky-700',
    }

    return (
        <span className={`inline-flex rounded-full border px-2.5 py-1 text-xs font-semibold ${palette[tone] || palette.slate}`}>
            {label || value || 'Unknown'}
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
        case 'SyntheticQaCollected':
            return 'amber'
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

function validationMessage(t, key, fallback) {
    return typeof t === 'function' ? t(`orders.management.validation.${key}`) : fallback
}

export function validateManualDeliveryEvidence(evidence, t) {
    const deliveryReference = evidence?.deliveryReference?.trim() || ''
    const proofOfDeliveryUrl = evidence?.proofOfDeliveryUrl?.trim() || ''
    const recipientName = evidence?.recipientName?.trim() || ''

    if (!deliveryReference) return validationMessage(t, 'deliveryReferenceRequired', 'Delivery reference is required.')
    if (deliveryReference.length > MAX_DELIVERY_REFERENCE_LENGTH) return validationMessage(t, 'deliveryReferenceTooLong', 'Delivery reference is too long.')
    if (!proofOfDeliveryUrl) return validationMessage(t, 'proofUrlRequired', 'Proof of delivery URL is required.')
    if (proofOfDeliveryUrl.length > MAX_PROOF_URL_LENGTH) return validationMessage(t, 'proofUrlTooLong', 'Proof of delivery URL is too long.')
    if (/\s/.test(proofOfDeliveryUrl)) return validationMessage(t, 'proofUrlWhitespace', 'Proof of delivery URL cannot contain whitespace.')
    if (recipientName.length > MAX_RECIPIENT_NAME_LENGTH) return validationMessage(t, 'recipientNameTooLong', 'Recipient name is too long.')

    try {
        const parsed = new URL(proofOfDeliveryUrl)
        if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
            return validationMessage(t, 'proofUrlAbsolute', 'Proof of delivery must be an absolute HTTP or HTTPS URL.')
        }
    } catch {
        return validationMessage(t, 'proofUrlAbsolute', 'Proof of delivery must be an absolute HTTP or HTTPS URL.')
    }

    return null
}

export function buildManualDispatchPayload(shipmentId, trackingNumber) {
    const tracking = trackingNumber?.trim()
    if (!shipmentId || !tracking || tracking.length > 100 || containsAsciiControl(tracking)) {
        throw new Error('A shipment and supported tracking reference are required for dispatch.')
    }
    return { shipmentId, trackingNumber: tracking }
}

export function validateReturnReceiptEvidence(evidence, requestedQuantity, t) {
    const resellableQuantity = Number(evidence?.resellableQuantity)
    const damagedQuantity = Number(evidence?.damagedQuantity)
    const maximumQuantity = Number(requestedQuantity)

    if (!Number.isInteger(resellableQuantity) || !Number.isInteger(damagedQuantity)) {
        return validationMessage(t, 'receiptWholeNumbers', 'Receipt quantities must be whole numbers.')
    }
    if (resellableQuantity < 0 || damagedQuantity < 0) {
        return validationMessage(t, 'receiptNonNegative', 'Receipt quantities cannot be negative.')
    }
    if (resellableQuantity + damagedQuantity <= 0) {
        return validationMessage(t, 'receiptOneRequired', 'At least one received item is required.')
    }
    if (!Number.isInteger(maximumQuantity) || maximumQuantity <= 0 || resellableQuantity + damagedQuantity > maximumQuantity) {
        return validationMessage(t, 'receiptExceedsApproved', 'Receipt quantities cannot exceed the approved return quantity.')
    }

    return null
}

export function getLifecycleMutationErrorMessage(error, t) {
    return error?.status === 409
        ? validationMessage(t, 'staleOrder', 'The order changed. Canonical data was refreshed; review it before trying again.')
        : error?.message || validationMessage(t, 'lifecycleFailed', 'The lifecycle action could not be completed.')
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

function actionLabel(t, action) {
    const code = action?.code || action?.actionCode
    return code
        ? t(`orders.management.actions.${code}`, { defaultValue: action?.label || code })
        : action?.label
}

function SectionCard({ title, children }) {
    return (
        <section className="rounded-3xl border border-slate-200 bg-white p-5 shadow-sm">
            <h3 className="text-sm font-semibold uppercase tracking-[0.18em] text-slate-500">{title}</h3>
            <div className="mt-4">{children}</div>
        </section>
    )
}

export function OrderDetailsPanel({
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
    administrativeActionResult,
    onConfirmReturnReceipt,
    isReturnReceiptUpdating,
    returnReceiptError,
    customerTrust,
    isCustomerTrustLoading,
    customerTrustError,
    onRetryCustomerTrust,
    onCustomerPhoneAction,
    isCustomerPhoneActionUpdating,
    customerPhoneActionError,
    sessionIntelligence,
    isSessionIntelligenceLoading,
    sessionIntelligenceError,
    onRetrySessionIntelligence,
}) {
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage === 'ar' ? 'ar-EG' : 'en-US'
    const closeButtonRef = useRef(null)
    const [statusCandidate, setStatusCandidate] = useState(order?.status || '')
    const [statusReason, setStatusReason] = useState('')
    const [pendingFulfillmentAction, setPendingFulfillmentAction] = useState(null)
    const [fulfillmentEvidence, setFulfillmentEvidence] = useState({
        trackingNumber: '',
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
    useEffect(() => {
        if (administrativeActionResult?.outcome === 'Confirmed' || order?.shipment?.cancellation?.outcome === 'Confirmed')
            setPendingAdministrativeAction(null)
    }, [administrativeActionResult, order?.shipment?.cancellation])
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
    const shipmentActions = order?.shipment?.cancellation || administrativeActionResult?.outcome ? [] : (order?.availableShipmentActions || []).filter((action) => action?.code === 'cancel-shipment')
    const checkoutFieldItems = presentOrderCheckoutFields(order?.checkoutFieldSnapshots)
    const riskPresentation = customerTrust?.phoneRisk?.isBlocked
        ? { label: t('orders.phoneBlockedStatus'), tone: 'rose' }
        : customerRiskPresentation(t, customerTrust?.hasPhone ? customerTrust.riskLevel : 'UNAVAILABLE')

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
            trackingNumber: '',
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

        const payload = pendingFulfillmentAction.code === 'out-for-delivery'
            ? buildManualDispatchPayload(order.shipment?.shipmentId, fulfillmentEvidence.trackingNumber)
            : pendingFulfillmentAction.code === 'delivered'
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
        ? validateManualDeliveryEvidence(fulfillmentEvidence, t)
        : null
    const canSubmitFulfillmentEvidence = pendingFulfillmentAction?.code === 'delivered'
        ? !deliveryEvidenceError
        : pendingFulfillmentAction?.code === 'out-for-delivery'
            ? Boolean(order.shipment?.shipmentId && fulfillmentEvidence.trackingNumber.trim() && fulfillmentEvidence.trackingNumber.trim().length <= 100 && !containsAsciiControl(fulfillmentEvidence.trackingNumber))
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
        if (pendingAdministrativeAction.code === 'cancel-shipment' && (order?.shipment?.cancellation || administrativeActionResult?.outcome)) return
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
        if (!pendingReturnReceipt || validateReturnReceiptEvidence(returnReceiptEvidence, pendingReturnReceipt.requestedQuantity, t)) {
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
        ? validateReturnReceiptEvidence(returnReceiptEvidence, pendingReturnReceipt.requestedQuantity, t)
        : null

    useEffect(() => {
        setStatusCandidate(order?.status || '')
        setStatusReason('')
        setPendingFulfillmentAction(null)
        setFulfillmentEvidence({
            trackingNumber: '',
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
                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.title')}</p>
                        <h2 id="order-details-heading" className="mt-2 text-xl font-semibold text-slate-950">
                            {order?.orderNumber || t('orders.management.details.loadingOrder')}
                        </h2>
                    </div>
                    <button
                        ref={closeButtonRef}
                        type="button"
                        onClick={onClose}
                        className="rounded-full border border-slate-200 p-2 text-slate-500 transition hover:border-slate-300 hover:text-slate-900"
                        aria-label={t('orders.management.details.close')}
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
                            <p className="font-semibold">{t('orders.management.details.loadFailed')}</p>
                            <p className="mt-2">{error?.message || t('orders.management.details.tryAgain')}</p>
                            <button
                                type="button"
                                onClick={onRetry}
                                disabled={isRetrying}
                                className="mt-4 inline-flex items-center gap-2 rounded-full border border-rose-200 bg-white px-4 py-2 text-sm font-semibold text-rose-700 transition hover:border-rose-300 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                <RefreshCw className={`h-4 w-4 ${isRetrying ? 'animate-spin' : ''}`} />
                                {t('common.retry')}
                            </button>
                        </div>
                    ) : null}

                    {!isLoading && !isError && order ? (
                        <div className="space-y-5">
                            {!order.isMarketplaceOrder ? <SectionCard title={t('orders.management.details.lifecycle')}>
                                <div className="grid gap-4 md:grid-cols-2">
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.orderStatus')}</p>
                                        <div className="mt-2">
                                            <StatusBadge value={order.status} label={t(`orders.status.${order.status}`, { defaultValue: order.status })} tone={resolveOrderTone(order.status)} />
                                        </div>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.paymentStatus')}</p>
                                        <div className="mt-2">
                                            <StatusBadge value={order.paymentStatus} label={t(`orders.payment.${order.paymentStatus}`, { defaultValue: order.paymentStatus })} tone={resolvePaymentTone(order.paymentStatus)} />
                                        </div>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.shipmentStatus')}</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{order.shipmentStatus || t('orders.management.details.noShipment')}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.created')}</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{formatDateTime(order.createdAtUtc, locale, t('common.unavailable'))}</p>
                                    </div>
                                </div>

                                <form className="mt-5 rounded-2xl border border-slate-200 bg-slate-50 p-4" onSubmit={handleSubmitStatus}>
                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                        {t('orders.management.details.requestStatusChange')}
                                    </p>
                                    <div className="mt-3 grid gap-3 md:grid-cols-[1fr_1.4fr_auto] md:items-end">
                                        <label className="flex flex-col gap-2 text-sm text-slate-700">
                                            <span className="font-medium">{t('orders.management.details.requestedStatus')}</span>
                                            <select
                                                value={statusCandidate}
                                                onChange={(event) => setStatusCandidate(event.target.value)}
                                                disabled={!order.rowVersion || isStatusUpdating}
                                                className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100 disabled:cursor-not-allowed disabled:bg-slate-100"
                                            >
                                                {directStatusOptions.map((option) => (
                                                    <option key={option} value={option}>{t(`orders.status.${option}`, { defaultValue: option })}</option>
                                                ))}
                                            </select>
                                        </label>
                                        <label className="flex flex-col gap-2 text-sm text-slate-700">
                                            <span className="font-medium">{t('orders.management.details.reason')}</span>
                                            <input
                                                value={statusReason}
                                                onChange={(event) => setStatusReason(event.target.value)}
                                                disabled={!order.rowVersion || isStatusUpdating}
                                                placeholder={t('orders.management.details.auditNote')}
                                                className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100 disabled:cursor-not-allowed disabled:bg-slate-100"
                                            />
                                        </label>
                                        <button
                                            type="submit"
                                            disabled={!canSubmitStatusCandidate || isStatusUpdating}
                                            className="inline-flex items-center justify-center rounded-2xl bg-slate-950 px-4 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:bg-slate-300"
                                        >
                                            {isStatusUpdating ? t('orders.management.details.submitting') : t('orders.management.details.submitCandidate')}
                                        </button>
                                    </div>
                                    {!order.rowVersion ? (
                                        <p className="mt-3 text-sm text-amber-700">
                                            {t('orders.management.details.rowVersionRequired')}
                                        </p>
                                    ) : null}
                                    {statusUpdateError ? (
                                        <p className="mt-3 text-sm text-rose-700">{statusUpdateError}</p>
                                    ) : null}
                                    <p className="mt-3 text-sm text-slate-600">
                                        {t('orders.management.details.dedicatedActions')}
                                    </p>
                                </form>

                                <div className="mt-5 rounded-2xl border border-slate-200 bg-white p-4">
                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                        {t('orders.management.details.fulfillmentLifecycle')}
                                    </p>
                                    <div className="mt-3 grid gap-4 md:grid-cols-2">
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.fulfillmentStatus')}</p>
                                            <p className="mt-2 text-sm font-medium text-slate-900">{order.fulfillmentStatus || fulfillmentAuthority?.currentStatus || t('common.unavailable')}</p>
                                        </div>
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.authority')}</p>
                                            <p className="mt-2 text-sm font-medium text-slate-900">
                                                {isFulfillmentAuthorityLoading ? t('orders.management.details.loadingActions') : t('orders.management.details.availableActions', { count: fulfillmentActions.length })}
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
                                                    title={executable ? actionLabel(t, action) : t('orders.management.details.unsupportedAction')}
                                                    className={`rounded-full border px-4 py-2 text-sm font-semibold transition disabled:cursor-not-allowed disabled:opacity-50 ${pendingFulfillmentAction?.code === action.code ? 'border-sky-300 bg-sky-50 text-sky-800' : 'border-slate-200 text-slate-700 hover:border-slate-300 hover:text-slate-950'}`}
                                                >
                                                    {isFulfillmentActionUpdating ? t('orders.management.details.working') : actionLabel(t, action)}
                                                </button>
                                            )
                                        }) : (
                                            <p className="text-sm text-slate-500">
                                                {t('orders.management.details.noFulfillmentActions')}
                                            </p>
                                        )}
                                    </div>

                                    {pendingFulfillmentAction ? (
                                        <form className="mt-4 rounded-2xl border border-sky-200 bg-sky-50 p-4" onSubmit={handleSubmitFulfillmentEvidence}>
                                            <p className="text-sm font-semibold text-sky-950">{actionLabel(t, pendingFulfillmentAction)}</p>
                                            <p className="mt-1 text-xs text-sky-800">
                                                {t('orders.management.details.submitEvidence')}
                                            </p>

                                            {pendingFulfillmentAction.code === 'out-for-delivery' ? (
                                                <label className="mt-4 flex flex-col gap-2 text-sm text-slate-700">
                                                    <span className="font-medium">{t('orders.trackingNumber')}</span>
                                                    <input autoFocus required maxLength={100} value={fulfillmentEvidence.trackingNumber} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, trackingNumber: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                                </label>
                                            ) : pendingFulfillmentAction.code === 'delivered' ? (
                                                <div className="mt-4 grid gap-3 md:grid-cols-2">
                                                    <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                        <span className="font-medium">{t('orders.management.details.deliveryReference')}</span>
                                                        <input autoFocus required maxLength={MAX_DELIVERY_REFERENCE_LENGTH} value={fulfillmentEvidence.deliveryReference} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, deliveryReference: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-sky-400 focus:ring-2 focus:ring-sky-100" />
                                                    </label>
                                                    <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                        <span className="font-medium">{t('orders.management.details.proofUrl')}</span>
                                                        <input required type="url" inputMode="url" maxLength={MAX_PROOF_URL_LENGTH} aria-describedby="delivery-proof-error" value={fulfillmentEvidence.proofOfDeliveryUrl} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, proofOfDeliveryUrl: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-sky-400 focus:ring-2 focus:ring-sky-100" />
                                                    </label>
                                                    <label className="flex flex-col gap-2 text-sm text-slate-700 md:col-span-2">
                                                        <span className="font-medium">{t('orders.management.details.recipientName')}</span>
                                                        <input maxLength={MAX_RECIPIENT_NAME_LENGTH} value={fulfillmentEvidence.recipientName} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, recipientName: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-sky-400 focus:ring-2 focus:ring-sky-100" />
                                                    </label>
                                                    {deliveryEvidenceError ? (
                                                        <p id="delivery-proof-error" role="alert" className="text-sm text-rose-700 md:col-span-2">{deliveryEvidenceError}</p>
                                                    ) : null}
                                                </div>
                                            ) : (
                                                <div className="mt-4 grid gap-3 md:grid-cols-2">
                                                    <label className="flex flex-col gap-2 text-sm text-slate-700 md:col-span-2">
                                                        <span className="font-medium">{t('orders.management.details.reason')}</span>
                                                        <textarea autoFocus required maxLength={MAX_LIFECYCLE_REASON_LENGTH} value={fulfillmentEvidence.reason} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, reason: event.target.value }))} className="min-h-24 rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-sky-400 focus:ring-2 focus:ring-sky-100" />
                                                    </label>
                                                    {pendingFulfillmentAction.code === 'failed-delivery' ? (
                                                        <>
                                                            <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                                <span className="font-medium">{t('orders.management.details.attemptNumber')}</span>
                                                                <input type="number" min="1" value={fulfillmentEvidence.attemptNumber} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, attemptNumber: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-sky-400 focus:ring-2 focus:ring-sky-100" />
                                                            </label>
                                                            <label className="flex items-center gap-2 self-end rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-700">
                                                                <input type="checkbox" checked={fulfillmentEvidence.canRetry} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, canRetry: event.target.checked }))} />
                                                                {t('orders.management.details.canRetryDelivery')}
                                                            </label>
                                                        </>
                                                    ) : (
                                                        <label className="flex flex-col gap-2 text-sm text-slate-700 md:col-span-2">
                                                            <span className="font-medium">{t('orders.management.details.returnAuthorization')}</span>
                                                            <input value={fulfillmentEvidence.returnAuthorizationNumber} onChange={(event) => setFulfillmentEvidence((current) => ({ ...current, returnAuthorizationNumber: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-sky-400 focus:ring-2 focus:ring-sky-100" />
                                                        </label>
                                                    )}
                                                </div>
                                            )}

                                            <div className="mt-4 flex flex-wrap gap-2">
                                                <button type="submit" disabled={!canSubmitFulfillmentEvidence || isFulfillmentActionUpdating} className="rounded-full bg-slate-950 px-4 py-2 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-300">
                                                    {isFulfillmentActionUpdating ? t('orders.management.details.submitting') : t('orders.management.details.confirmNamed', { action: actionLabel(t, pendingFulfillmentAction) })}
                                                </button>
                                                <button type="button" onClick={resetFulfillmentEvidence} disabled={isFulfillmentActionUpdating} className="rounded-full border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-700 disabled:cursor-not-allowed disabled:opacity-50">
                                                    {t('common.cancel')}
                                                </button>
                                            </div>
                                        </form>
                                    ) : null}
                                </div>
                            </SectionCard> : <section className="rounded-3xl border border-blue-200 bg-blue-50 p-5">
                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-blue-700">{t('orders.management.details.commerceHubBadge')}</p>
                                <h3 className="mt-2 text-lg font-semibold text-slate-950">{t('orders.management.details.commerceHubTitle')}</h3>
                                <p className="mt-2 text-sm leading-6 text-slate-600">{t('orders.management.details.commerceHubDescription')}</p>
                                <Link to={`/commercehub/orders/${order.orderId}`} className="mt-4 inline-flex rounded-full bg-blue-700 px-4 py-2 text-sm font-semibold text-white">{t('orders.management.details.openCommerceHub')}</Link>
                            </section>}

                            {!order.isMarketplaceOrder ? (
                                <SectionCard title={t('orders.management.details.paymentLifecycle')}>
                                    {order.isSyntheticQa === true && <p role="note" className="mb-4 rounded-xl border border-amber-300 bg-amber-50 p-3 font-semibold text-amber-950">{t('orders.payment.SyntheticQaCollected')} — SYNTHETIC QA — NO REAL DELIVERY OR MONEY</p>}
                                    <div className="grid gap-4 md:grid-cols-2">
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.canonicalPaymentStatus')}</p>
                                            <div className="mt-2"><StatusBadge value={order.paymentStatus} label={t(`orders.payment.${order.paymentStatus}`, { defaultValue: order.paymentStatus })} tone={resolvePaymentTone(order.paymentStatus)} /></div>
                                        </div>
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.paymentMethod')}</p>
                                            <p className="mt-2 text-sm font-medium text-slate-900">{paymentAuthority?.paymentMethodCode || t('common.unavailable')}</p>
                                        </div>
                                    </div>
                                    {paymentAuthority?.statusMessage ? <p className="mt-3 text-sm text-slate-600">{paymentAuthority.statusMessage}</p> : null}
                                    {paymentAuthorityError ? <p role="alert" className="mt-3 text-sm text-rose-700">{paymentAuthorityError}</p> : null}
                                    {paymentActionError ? <p role="alert" className="mt-3 text-sm text-rose-700">{paymentActionError}</p> : null}
                                    <div className="mt-4 flex flex-wrap gap-2">
                                        {isPaymentAuthorityLoading ? <p role="status" className="text-sm text-slate-500">{t('orders.management.details.loadingPaymentActions')}</p> : null}
                                        {!isPaymentAuthorityLoading && paymentActions.length ? paymentActions.map((action) => {
                                            const executable = isPaymentActionExecutableFromOrdersDrawer(action)
                                            return (
                                                <button
                                                    key={action.actionCode}
                                                    type="button"
                                                    onClick={() => handlePaymentActionSelection(action)}
                                                    disabled={!executable || isPaymentActionUpdating}
                                                    aria-pressed={pendingPaymentAction?.actionCode === action.actionCode}
                                                    title={t(`orders.management.actionDescriptions.${action.actionCode}`, { defaultValue: action.description })}
                                                    className="rounded-full border border-slate-200 px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 disabled:cursor-not-allowed disabled:opacity-50"
                                                >
                                                    {isPaymentActionUpdating ? t('orders.management.details.working') : actionLabel(t, action)}
                                                </button>
                                            )
                                        }) : null}
                                        {!isPaymentAuthorityLoading && !paymentActions.length ? <p className="text-sm text-slate-500">{t('orders.management.details.noPaymentActions')}</p> : null}
                                    </div>

                                    {pendingPaymentAction ? (
                                        <form className="mt-4 rounded-2xl border border-amber-200 bg-amber-50 p-4" onSubmit={handleSubmitPaymentAction}>
                                            <p className="text-sm font-semibold text-amber-950">{actionLabel(t, pendingPaymentAction)}</p>
                                            <p className="mt-1 text-xs text-amber-800">{t(`orders.management.actionDescriptions.${pendingPaymentAction.actionCode}`, { defaultValue: pendingPaymentAction.description })}</p>
                                            {pendingPaymentAction.actionCode === 'refund' ? (
                                                <div className="mt-4 grid gap-3 md:grid-cols-2">
                                                    <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                        <span className="font-medium">{t('orders.management.details.refundAmount')}</span>
                                                        <input autoFocus required type="number" min="0.01" step="0.01" value={paymentEvidence.amount} onChange={(event) => setPaymentEvidence((current) => ({ ...current, amount: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                                    </label>
                                                    <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                        <span className="font-medium">{t('orders.management.details.refundReason')}</span>
                                                        <input maxLength={MAX_LIFECYCLE_REASON_LENGTH} value={paymentEvidence.reason} onChange={(event) => setPaymentEvidence((current) => ({ ...current, reason: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                                    </label>
                                                    <p className="text-xs text-amber-800 md:col-span-2">{t('orders.management.details.refundScope')}</p>
                                                </div>
                                            ) : (
                                                <label className="mt-4 flex flex-col gap-2 text-sm text-slate-700">
                                                    <span className="font-medium">{t('orders.management.details.collectionReference')}</span>
                                                    <input autoFocus maxLength={MAX_DELIVERY_REFERENCE_LENGTH} value={paymentEvidence.deliveryReference} onChange={(event) => setPaymentEvidence((current) => ({ ...current, deliveryReference: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                                </label>
                                            )}
                                            <div className="mt-4 flex flex-wrap gap-2">
                                                <button type="submit" disabled={!canSubmitPaymentAction || isPaymentActionUpdating} className="rounded-full bg-slate-950 px-4 py-2 text-sm font-semibold text-white disabled:bg-slate-300">{isPaymentActionUpdating ? t('orders.management.details.submitting') : t('orders.management.details.confirmNamed', { action: actionLabel(t, pendingPaymentAction) })}</button>
                                                <button type="button" disabled={isPaymentActionUpdating} onClick={() => setPendingPaymentAction(null)} className="rounded-full border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-700 disabled:opacity-50">{t('common.cancel')}</button>
                                            </div>
                                        </form>
                                    ) : null}
                                </SectionCard>
                            ) : null}

                            <SectionCard title={t('orders.management.details.shipmentEvidence')}>
                                <CancellationOutcome result={order?.shipment?.cancellation || administrativeActionResult} />
                                {order.shipment ? (
                                    <div className="grid gap-4 md:grid-cols-2">
                                        <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.shipmentStatus')}</p><p className="mt-2 text-sm font-medium text-slate-900">{order.shipment.status}</p></div>
                                        <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.carrier')}</p><p className="mt-2 text-sm font-medium text-slate-900">{order.shipment.carrierName || order.shipment.shippingMethodName || t('orders.management.details.manualUnavailable')}</p></div>
                                        <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.shipmentNumber')}</p><p className="mt-2 break-all text-sm font-medium text-slate-900">{order.shipment.shipmentNumber}</p></div>
                                        <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.trackingNumber')}</p><p className="mt-2 break-all text-sm font-medium text-slate-900">{order.shipment.trackingNumber || t('common.unavailable')}</p></div>
                                        {isSafeTrackingUrl(order.shipment.trackingUrl) ? <a className="text-sm font-semibold text-sky-700 underline md:col-span-2" href={order.shipment.trackingUrl} target="_blank" rel="noreferrer">{t('orders.management.details.openTracking')}</a> : null}
                                    </div>
                                ) : <p className="text-sm text-slate-500">{t('orders.management.details.noShipment')}</p>}
                            </SectionCard>

                            {(orderActions.length || shipmentActions.length) ? (
                                <SectionCard title={t('orders.management.details.administrativeCommands')}>
                                    {administrativeActionError ? <p role="alert" className="mb-3 text-sm text-rose-700">{administrativeActionError}</p> : null}
                                    <div className="flex flex-wrap gap-2">
                                        {[...orderActions, ...shipmentActions].map((action) => (
                                            <button key={action.code} type="button" disabled={isAdministrativeActionUpdating} onClick={() => { setPendingAdministrativeAction(action); setAdministrativeReason('') }} className="rounded-full border border-rose-200 px-4 py-2 text-sm font-semibold text-rose-700 disabled:opacity-50">{actionLabel(t, action)}</button>
                                        ))}
                                    </div>
                                    {pendingAdministrativeAction ? (
                                        <form className="mt-4 rounded-2xl border border-rose-200 bg-rose-50 p-4" onSubmit={handleSubmitAdministrativeAction}>
                                            <p className="text-sm font-semibold text-rose-950">{t('orders.management.details.confirmNamed', { action: actionLabel(t, pendingAdministrativeAction) })}</p>
                                            <p className="mt-1 text-xs text-rose-800">{t('orders.management.details.administrativeSeparation')}</p>
                                            <label className="mt-4 flex flex-col gap-2 text-sm text-slate-700">
                                                <span className="font-medium">{t('orders.management.details.reason')}</span>
                                                <textarea autoFocus required maxLength={MAX_LIFECYCLE_REASON_LENGTH} value={administrativeReason} onChange={(event) => setAdministrativeReason(event.target.value)} className="min-h-24 rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                            </label>
                                            <div className="mt-4 flex gap-2">
                                                <button type="submit" disabled={!administrativeReason.trim() || isAdministrativeActionUpdating} className="rounded-full bg-rose-700 px-4 py-2 text-sm font-semibold text-white disabled:bg-slate-300">{isAdministrativeActionUpdating ? t('orders.management.details.submitting') : t('orders.management.details.confirmNamed', { action: actionLabel(t, pendingAdministrativeAction) })}</button>
                                                <button type="button" disabled={isAdministrativeActionUpdating} onClick={() => setPendingAdministrativeAction(null)} className="rounded-full border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-700">{t('orders.management.details.keepCurrentState')}</button>
                                            </div>
                                        </form>
                                    ) : null}
                                </SectionCard>
                            ) : null}

                            <SectionCard title={t('orders.customer')}>
                                <div className="grid gap-4 md:grid-cols-2">
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('common.name')}</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{order.customer?.name || t('common.unavailable')}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('common.email')}</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{order.customer?.email || t('common.unavailable')}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('common.phone')}</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{order.customer?.phone || t('common.unavailable')}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.sourceLabel')}</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{getOrderSourcePresentation(order).label}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.customerIdentity')}</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{t(`orders.customerIdentity.${order.customer?.identityType || 'GUEST'}`)}</p>
                                    </div>
                                </div>
                            </SectionCard>

                            <SectionCard title={t('orders.management.details.customerTrust')}>
                                {isCustomerTrustLoading ? (
                                    <p role="status" className="text-sm text-slate-500">{t('orders.management.details.loadingCustomerTrust')}</p>
                                ) : null}
                                {customerTrustError ? (
                                    <div role="alert" className="rounded-2xl border border-rose-200 bg-rose-50 p-4 text-sm text-rose-700">
                                        <p>{customerTrustError}</p>
                                        <button type="button" onClick={onRetryCustomerTrust} className="mt-3 font-semibold underline">{t('common.retry')}</button>
                                    </div>
                                ) : null}
                                {!isCustomerTrustLoading && !customerTrustError && customerTrust ? (
                                    <div className="space-y-4">
                                        <div className="flex flex-wrap items-center justify-between gap-3">
                                            <div className="flex items-center gap-2">
                                                <ShieldCheck className="h-5 w-5 text-slate-500" />
                                                <StatusBadge value={customerTrust.phoneRisk?.isBlocked ? 'blocked' : customerTrust.riskLevel} label={riskPresentation.label} tone={riskPresentation.tone} />
                                            </div>
                                        </div>
                                        <p className="text-sm text-slate-500">{t('orders.customerRisk.description')}</p>

                                        {customerTrust.hasPhone ? (
                                            <div className="grid gap-3 sm:grid-cols-2">
                                                <div className="rounded-2xl bg-slate-50 p-4">
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.previousOrders')}</p>
                                                    <p className="mt-2 text-xl font-semibold text-slate-950">{customerTrust.previousOrdersCount}</p>
                                                </div>
                                                <div className="rounded-2xl bg-slate-50 p-4">
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.canceledOrders')}</p>
                                                    <p className="mt-2 text-xl font-semibold text-slate-950">{customerTrust.cancelledOrdersCount}</p>
                                                </div>
                                                <div className="rounded-2xl bg-slate-50 p-4">
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.storeSpamReports')}</p>
                                                    <p className="mt-2 text-xl font-semibold text-slate-950">{customerTrust.phoneRisk?.spamReports || 0}</p>
                                                </div>
                                                <div className="rounded-2xl bg-slate-50 p-4">
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.globalSpamReports')}</p>
                                                    <p className="mt-2 text-xl font-semibold text-slate-950">{customerTrust.phoneRisk?.globalSpamReports || 0}</p>
                                                </div>
                                            </div>
                                        ) : (
                                            <p className="text-sm text-slate-500">{t('orders.management.details.noCustomerPhone')}</p>
                                        )}

                                        {customerTrust.riskSignals?.length ? (
                                            <ul className="space-y-2 text-sm text-amber-800">
                                                {customerTrust.riskSignals.map((signal) => (
                                                    <li key={signal} className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3">
                                                        {t(`orders.customerRiskSignals.${signal}`, { defaultValue: signal })}
                                                    </li>
                                                ))}
                                            </ul>
                                        ) : null}

                                        {customerPhoneActionError ? <p role="alert" className="text-sm text-rose-700">{customerPhoneActionError}</p> : null}
                                        {customerTrust.hasPhone ? (
                                            <div className="flex flex-wrap gap-2">
                                                <button
                                                    type="button"
                                                    disabled={isCustomerPhoneActionUpdating}
                                                    onClick={() => onCustomerPhoneAction({
                                                        action: customerTrust.phoneRisk?.isBlocked ? 'unblock' : 'block',
                                                        reason: t('orders.blockPhoneDefaultReason', { orderNumber: order.orderNumber }),
                                                    })}
                                                    className="inline-flex items-center gap-2 rounded-full border border-slate-300 px-4 py-2 text-sm font-semibold text-slate-700 disabled:opacity-50"
                                                >
                                                    <Ban className="h-4 w-4" />
                                                    {customerTrust.phoneRisk?.isBlocked ? t('orders.unblockPhone') : t('orders.blockPhone')}
                                                </button>
                                                <button
                                                    type="button"
                                                    disabled={isCustomerPhoneActionUpdating || customerTrust.hasReportedSpam}
                                                    onClick={() => onCustomerPhoneAction({ action: 'reportSpam' })}
                                                    className="inline-flex items-center gap-2 rounded-full border border-rose-200 px-4 py-2 text-sm font-semibold text-rose-700 disabled:opacity-50"
                                                >
                                                    <Flag className="h-4 w-4" />
                                                    {customerTrust.hasReportedSpam ? t('orders.spamReported') : t('orders.reportSpam')}
                                                </button>
                                                <Link to={`/ecommerce/blocked-phones?search=${encodeURIComponent(customerTrust.phoneNumber)}`} className="inline-flex items-center rounded-full px-4 py-2 text-sm font-semibold text-indigo-700 underline">
                                                    {t('orders.manageBlockedPhones')}
                                                </Link>
                                            </div>
                                        ) : null}
                                    </div>
                                ) : null}
                            </SectionCard>

                            <SectionCard title={t('orders.sessionDetails')}>
                                {isSessionIntelligenceLoading ? <p role="status" className="text-sm text-slate-500">{t('orders.loadingSessionJourney')}</p> : null}
                                {sessionIntelligenceError ? (
                                    <div role="alert" className="rounded-2xl border border-rose-200 bg-rose-50 p-4 text-sm text-rose-700">
                                        <p>{sessionIntelligenceError}</p>
                                        <button type="button" onClick={onRetrySessionIntelligence} className="mt-3 font-semibold underline">{t('common.retry')}</button>
                                    </div>
                                ) : null}
                                {!isSessionIntelligenceLoading && !sessionIntelligenceError && !sessionIntelligence ? (
                                    <p className="text-sm text-slate-500">{t('orders.noSessionJourney')}</p>
                                ) : null}
                                {sessionIntelligence ? (
                                    <div className="space-y-4">
                                        <div className="grid gap-4 sm:grid-cols-2">
                                            <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.sessionJourney.source')}</p><p className="mt-2 text-sm font-medium text-slate-900">{[sessionIntelligence.source, sessionIntelligence.medium].filter(Boolean).join(' / ') || t('orders.sessionJourney.direct')}</p></div>
                                            <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.sessionJourney.device')}</p><p className="mt-2 text-sm font-medium text-slate-900">{sessionIntelligence.device || t('orders.sessionJourney.unknown')}</p></div>
                                            <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.sessionJourney.timeSpent')}</p><p className="mt-2 text-sm font-medium text-slate-900">{formatJourneyDuration(sessionIntelligence.durationSeconds, t)}</p></div>
                                            <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.sessionJourney.sessionsCount')}</p><p className="mt-2 text-sm font-medium text-slate-900">{sessionIntelligence.sessionsCount}</p></div>
                                            <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.sessionJourney.firstVisit')}</p><p className="mt-2 text-sm font-medium text-slate-900">{formatDateTime(sessionIntelligence.firstVisitAtUtc, locale, t('common.unavailable'))}</p></div>
                                            <div><p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.sessionJourney.checkoutVisit')}</p><p className="mt-2 text-sm font-medium text-slate-900">{formatDateTime(sessionIntelligence.checkoutVisitAtUtc, locale, t('common.unavailable'))}</p></div>
                                            <div className="sm:col-span-2"><p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.sessionJourney.purchaseTime')}</p><p className="mt-2 text-sm font-medium text-slate-900">{formatDateTime(sessionIntelligence.purchaseCompletedAtUtc, locale, t('common.unavailable'))}</p></div>
                                        </div>
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.sessionJourney.visitedPages')}</p>
                                            {sessionIntelligence.visitedPages?.length ? (
                                                <ol className="mt-3 space-y-2" dir="ltr">
                                                    {sessionIntelligence.visitedPages.map((page, index) => <li key={`${page}-${index}`} className="break-all rounded-xl bg-slate-50 px-3 py-2 font-mono text-xs text-slate-700">{index + 1}. {page}</li>)}
                                                </ol>
                                            ) : <p className="mt-2 text-sm text-slate-500">{t('orders.sessionJourney.noVisitedPages')}</p>}
                                        </div>
                                    </div>
                                ) : null}
                            </SectionCard>

                            {getMarketplaceLineagePresentation(order) ? (
                                <SectionCard title={t('orders.management.details.marketplaceLineage')}>
                                    {(() => {
                                        const lineage = getMarketplaceLineagePresentation(order)
                                        return (
                                            <div className="grid gap-4 md:grid-cols-2">
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.marketplace')}</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{lineage.marketplaceName}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.marketplaceOrder')}</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{lineage.marketplaceOrderNumber}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.marketplaceIdentity')}</p>
                                                    <p className="mt-2 break-all text-sm font-medium text-slate-900">{lineage.marketplaceOrderId}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.vendorIdentity')}</p>
                                                    <p className="mt-2 break-all text-sm font-medium text-slate-900">{lineage.vendorId}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.vendor')}</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{lineage.vendorName}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.sourceType')}</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{lineage.marketplaceSourceType}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.marketplaceStatus')}</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{lineage.marketplaceStatus}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.paymentMetadata')}</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{lineage.marketplacePaymentStatus}</p>
                                                </div>
                                                {lineage.externalCustomerId ? (
                                                    <div className="md:col-span-2">
                                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.externalCustomerIdentity')}</p>
                                                        <p className="mt-2 break-all text-sm font-medium text-slate-900">{lineage.externalCustomerId}</p>
                                                    </div>
                                                ) : null}
                                            </div>
                                        )
                                    })()}
                                </SectionCard>
                            ) : null}

                            {getAffiliateAttributionPresentation(order) ? (
                                <SectionCard title={t('orders.management.details.affiliateAttribution')}>
                                    {(() => {
                                        const attribution = getAffiliateAttributionPresentation(order)
                                        return (
                                            <div className="grid gap-4 md:grid-cols-2">
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.affiliateIdentity')}</p>
                                                    <p className="mt-2 break-all text-sm font-medium text-slate-900">{attribution.affiliateAccountId}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.referralCode')}</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{attribution.referralCode}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.attributionSource')}</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{attribution.source}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.attributed')}</p>
                                                    <p className="mt-2 text-sm font-medium text-slate-900">{formatDateTime(attribution.attributedAtUtc, locale, t('common.unavailable'))}</p>
                                                </div>
                                                {attribution.promotionLinkId ? (
                                                    <div className="md:col-span-2">
                                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.promotionLink')}</p>
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
                                    {t('orders.management.details.affiliateWarning')}
                                </section>
                            ) : null}

                            <SectionCard title={t('common.totals')}>
                                <div className="grid gap-4 md:grid-cols-2">
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.subtotal')}</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{formatCurrency(order.totals?.subtotal, order.totals?.currency, locale)}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.discount')}</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{formatCurrency(order.totals?.discountAmount, order.totals?.currency, locale)}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.tax')}</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{formatCurrency(order.totals?.taxAmount, order.totals?.currency, locale)}</p>
                                    </div>
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.shipping')}</p>
                                        <p className="mt-2 text-sm font-medium text-slate-900">{formatCurrency(order.totals?.shippingCost, order.totals?.currency, locale)}</p>
                                    </div>
                                    <div className="md:col-span-2">
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.total')}</p>
                                        <p className="mt-2 text-lg font-semibold text-slate-950">{formatCurrency(order.totals?.total, order.totals?.currency, locale)}</p>
                                    </div>
                                </div>
                            </SectionCard>

                            <SectionCard title={t('orders.items')}>
                                {order.items?.length ? (
                                    <div className="space-y-3">
                                        {order.items.map((item) => (
                                            <div key={item.itemId} className="rounded-2xl border border-slate-200 px-4 py-4">
                                                <div className="flex items-start justify-between gap-4">
                                                    <div>
                                                        <p className="font-medium text-slate-950">{item.productName}</p>
                                                        <p className="mt-1 text-sm text-slate-500">
                                                            {item.variantName || item.sku || t('orders.management.details.noVariant')}
                                                        </p>
                                                    </div>
                                                    <div className="text-end">
                                                        <p className="text-sm font-semibold text-slate-950">
                                                            {formatCurrency(item.total, order.totals?.currency, locale)}
                                                        </p>
                                                        <p className="mt-1 text-xs text-slate-500">{t('orders.management.details.quantity', { count: item.quantity })}</p>
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
                                                                            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-amber-800">{t('orders.management.details.customerRefund')}</p>
                                                                            <p className="mt-2 text-sm font-medium text-slate-950">{refund.reason || t('orders.management.details.noReasonProvided')}</p>
                                                                            <p className="mt-1 text-xs text-slate-600">{t('orders.management.details.requestedAt', { date: formatDateTime(refund.createdAt, locale, t('common.unavailable')) })}</p>
                                                                        </div>
                                                                        <div className="text-end">
                                                                            <StatusBadge value={refund.status} tone="amber" />
                                                                            <p className="mt-2 text-sm font-semibold text-slate-950">
                                                                                {formatCurrency(refund.amount, order.totals?.currency, locale)} | {t('orders.management.details.quantity', { count: refund.quantity })}
                                                                            </p>
                                                                        </div>
                                                                    </div>

                                                                    {returnRequest ? (
                                                                        <div className="mt-4 rounded-2xl border border-slate-200 bg-white p-4">
                                                                            <div className="grid gap-3 md:grid-cols-2">
                                                                                <div>
                                                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.physicalReturn')}</p>
                                                                                    <p className="mt-2 text-sm font-semibold text-slate-950">{returnRequest.status}</p>
                                                                                </div>
                                                                                <div>
                                                                                    <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.approvedQuantity')}</p>
                                                                                    <p className="mt-2 text-sm font-semibold text-slate-950">{returnRequest.requestedQuantity}</p>
                                                                                </div>
                                                                                {returnRequest.courierName || returnRequest.trackingNumber ? (
                                                                                    <div className="md:col-span-2">
                                                                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.returnTracking')}</p>
                                                                                        <p className="mt-2 text-sm text-slate-700">
                                                                                            {[returnRequest.courierName, returnRequest.trackingNumber].filter(Boolean).join(' | ')}
                                                                                        </p>
                                                                                    </div>
                                                                                ) : null}
                                                                                {returnRequest.status === 'Confirmed' ? (
                                                                                    <div className="md:col-span-2">
                                                                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.receivedCondition')}</p>
                                                                                        <p className="mt-2 text-sm text-slate-700">
                                                                                            {t('orders.management.details.receivedConditionSummary', { resellable: returnRequest.receivedResellableQuantity, damaged: returnRequest.receivedDamagedQuantity })}
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
                                                                                    {t('orders.management.details.confirmReceivedCondition')}
                                                                                </button>
                                                                            ) : returnRequest.status !== 'Confirmed' ? (
                                                                                <p className="mt-4 text-sm text-slate-600">
                                                                                    {t('orders.management.details.receiptAvailability')}
                                                                                </p>
                                                                            ) : null}

                                                                            {isReceiptOpen ? (
                                                                                <form className="mt-4 rounded-2xl border border-sky-200 bg-sky-50 p-4" onSubmit={handleSubmitReturnReceipt}>
                                                                                    <p className="text-sm font-semibold text-sky-950">{t('orders.management.details.confirmReceivedInventory')}</p>
                                                                                    <p className="mt-1 text-xs text-sky-800">{t('orders.management.details.receiptOnlyScope')}</p>
                                                                                    <div className="mt-4 grid gap-3 md:grid-cols-2">
                                                                                        <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                                                            <span className="font-medium">{t('orders.management.details.resellableQuantity')}</span>
                                                                                            <input type="number" min="0" step="1" value={returnReceiptEvidence.resellableQuantity} onChange={(event) => setReturnReceiptEvidence((current) => ({ ...current, resellableQuantity: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                                                                        </label>
                                                                                        <label className="flex flex-col gap-2 text-sm text-slate-700">
                                                                                            <span className="font-medium">{t('orders.management.details.damagedQuantity')}</span>
                                                                                            <input type="number" min="0" step="1" value={returnReceiptEvidence.damagedQuantity} onChange={(event) => setReturnReceiptEvidence((current) => ({ ...current, damagedQuantity: event.target.value }))} className="rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                                                                        </label>
                                                                                        <label className="flex flex-col gap-2 text-sm text-slate-700 md:col-span-2">
                                                                                            <span className="font-medium">{t('orders.management.details.receiptNotes')}</span>
                                                                                            <textarea maxLength={MAX_LIFECYCLE_REASON_LENGTH} value={returnReceiptEvidence.notes} onChange={(event) => setReturnReceiptEvidence((current) => ({ ...current, notes: event.target.value }))} className="min-h-20 rounded-2xl border border-slate-200 bg-white px-4 py-3" />
                                                                                        </label>
                                                                                    </div>
                                                                                    {returnReceiptEvidenceError ? <p role="alert" className="mt-3 text-sm text-rose-700">{returnReceiptEvidenceError}</p> : null}
                                                                                    {returnReceiptError ? <p role="alert" className="mt-3 text-sm text-rose-700">{returnReceiptError}</p> : null}
                                                                                    <div className="mt-4 flex flex-wrap gap-2">
                                                                                        <button type="submit" disabled={Boolean(returnReceiptEvidenceError) || isReturnReceiptUpdating} className="rounded-full bg-slate-950 px-4 py-2 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-300">
                                                                                            {isReturnReceiptUpdating ? t('orders.management.details.confirmingReceipt') : t('orders.management.details.confirmReceipt')}
                                                                                        </button>
                                                                                        <button type="button" onClick={() => setPendingReturnReceipt(null)} disabled={isReturnReceiptUpdating} className="rounded-full border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-700 disabled:opacity-50">
                                                                                            {t('common.cancel')}
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
                                    <p className="text-sm text-slate-500">{t('orders.management.details.noItems')}</p>
                                )}
                            </SectionCard>

                            <SectionCard title={t('orders.management.details.payments')}>
                                {order.payments?.length ? (
                                    <div className="space-y-3">
                                        {order.payments.map((payment) => (
                                            <div key={payment.paymentIntentId || payment.paymentId} className="rounded-2xl border border-slate-200 px-4 py-4">
                                                <div className="flex flex-wrap items-start justify-between gap-3">
                                                    <div>
                                                        <p className="font-medium text-slate-950">
                                                            {payment.provider || payment.paymentMethod || t('orders.management.details.payment')}
                                                        </p>
                                                        <p className="mt-1 text-sm text-slate-500">
                                                            {payment.paymentReference || payment.providerReference || t('orders.management.details.noReference')}
                                                        </p>
                                                    </div>
                                                    <div className="text-end">
                                                        <StatusBadge
                                                            value={payment.status}
                                                            tone={resolvePaymentTone(payment.status)}
                                                        />
                                                        <p className="mt-2 text-sm font-semibold text-slate-950">
                                                            {formatCurrency(payment.amount, payment.currency, locale)}
                                                        </p>
                                                    </div>
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                ) : (
                                    <p className="text-sm text-slate-500">{t('orders.management.details.noPayments')}</p>
                                )}
                            </SectionCard>

                            <SectionCard title={t('orders.management.details.addressesAndNotes')}>
                                <div className="space-y-4 text-sm text-slate-700">
                                    <div>
                                        <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.shippingAddress')}</p>
                                        <p className="mt-2">
                                            {[
                                                order.shippingAddress?.address,
                                                order.shippingAddress?.city,
                                                order.shippingAddress?.state,
                                                order.shippingAddress?.country,
                                            ].filter(Boolean).join(', ') || t('common.unavailable')}
                                        </p>
                                    </div>
                                    {order.customerNotes ? (
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.customerNotes')}</p>
                                            <p className="mt-2 whitespace-pre-wrap">{order.customerNotes}</p>
                                        </div>
                                    ) : null}
                                    {order.adminNotes ? (
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.management.details.adminNotes')}</p>
                                            <p className="mt-2 whitespace-pre-wrap">{order.adminNotes}</p>
                                        </div>
                                    ) : null}
                                    {order.cancellationReason ? (
                                        <div>
                                            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('orders.cancellationReason')}</p>
                                            <p className="mt-2 whitespace-pre-wrap">{order.cancellationReason}</p>
                                        </div>
                                    ) : null}
                                </div>
                            </SectionCard>

                            {checkoutFieldItems.length > 0 ? (
                                <SectionCard title={t('orders.management.details.checkoutDetails')}>
                                    <dl className="space-y-4">
                                        {checkoutFieldItems.map((item) => (
                                            <div key={item.fieldId}>
                                                <dt className="text-xs uppercase tracking-[0.18em] text-slate-500">{item.label}</dt>
                                                <dd className="mt-2 whitespace-pre-wrap text-sm font-medium text-slate-900">{item.displayValue}</dd>
                                                {item.attachments.map((attachment) => (
                                                    <OrderCheckoutAttachmentPreview
                                                        key={attachment.attachmentId}
                                                        orderId={order.orderId}
                                                        fieldLabel={item.label}
                                                        attachment={attachment}
                                                    />
                                                ))}
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
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage === 'ar' ? 'ar-EG' : 'en-US'
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

    const customerTrustQuery = useQuery({
        queryKey: [...authorityScope, 'customer-trust', selectedOrderId],
        queryFn: ({ signal }) => getAdminOrderCustomerTrust(selectedOrderId, signal),
        enabled: Boolean(selectedOrderId && detailsQuery.data && detailsQuery.data.isMarketplaceOrder !== true),
        staleTime: 15_000,
    })

    const sessionIntelligenceQuery = useQuery({
        queryKey: [...authorityScope, 'session-intelligence', selectedOrderId],
        queryFn: ({ signal }) => getAdminOrderSessionIntelligence(selectedOrderId, signal),
        enabled: Boolean(selectedOrderId && detailsQuery.data && detailsQuery.data.isMarketplaceOrder !== true),
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
            queryClient.invalidateQueries({ queryKey: [...authorityScope, 'customer-trust', selectedOrderId] }),
            queryClient.invalidateQueries({ queryKey: [...authorityScope, 'session-intelligence', selectedOrderId] }),
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

    const customerPhoneActionInFlight = useRef(false)
    const customerPhoneActionMutation = useMutation({
        mutationFn: ({ orderId, action, reason }) => runAdminOrderCustomerPhoneAction(orderId, action, reason),
        onSuccess: (_, candidate) => queryClient.invalidateQueries({ queryKey: [...candidate.authorityScope, 'customer-trust'] }),
        onSettled: () => { customerPhoneActionInFlight.current = false },
    })

    function handleCustomerPhoneAction(action) {
        if (customerPhoneActionInFlight.current || !selectedOrderId) return
        if (action.action === 'unblock' && !globalThis.confirm(t('orders.unblockPhoneConfirm'))) {
            return
        }
        if (action.action === 'reportSpam' && !globalThis.confirm(t('orders.reportSpamConfirm'))) {
            return
        }
        customerPhoneActionInFlight.current = true
        customerPhoneActionMutation.mutate({ ...action, orderId: selectedOrderId, authorityScope })
    }

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
                            {t('orders.management.badge')}
                        </div>
                        <h1 className="mt-4 text-3xl font-semibold tracking-tight text-slate-950">{t('orders.title')}</h1>
                        <p className="mt-2 max-w-3xl text-sm leading-6 text-slate-600">
                            {t('orders.management.subtitle')}
                        </p>
                    </div>
                    <button
                        type="button"
                        onClick={() => ordersQuery.refetch()}
                        disabled={ordersQuery.isFetching}
                        className="inline-flex items-center justify-center gap-2 rounded-full border border-slate-200 bg-white px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:text-slate-950 disabled:cursor-not-allowed disabled:opacity-60"
                    >
                        <RefreshCw className={`h-4 w-4 ${ordersQuery.isFetching ? 'animate-spin' : ''}`} />
                        {t('common.refresh')}
                    </button>
                </div>

                <div className="mt-6 grid gap-4 md:grid-cols-2 xl:grid-cols-6">
                    <FilterField label={t('common.search')}>
                        <input
                            type="search"
                            value={filters.search}
                            onChange={(event) => handleFilterChange('search', event.target.value)}
                            placeholder={t('orders.management.searchPlaceholder')}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        />
                    </FilterField>

                    <FilterField label={t('orders.management.orderStatus')}>
                        <select
                            value={filters.status}
                            onChange={(event) => handleFilterChange('status', event.target.value)}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        >
                            {ORDER_STATUS_OPTIONS.map((option) => (
                                <option key={option || 'all'} value={option}>
                                    {option ? t(`orders.status.${option}`, option) : t('orders.management.allStatuses')}
                                </option>
                            ))}
                        </select>
                    </FilterField>

                    <FilterField label={t('orders.management.paymentStatus')}>
                        <select
                            value={filters.paymentStatus}
                            onChange={(event) => handleFilterChange('paymentStatus', event.target.value)}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        >
                            {PAYMENT_STATUS_OPTIONS.map((option) => (
                                <option key={option || 'all'} value={option}>
                                    {option ? t(`orders.payment.${option}`, option) : t('orders.management.allPaymentStates')}
                                </option>
                            ))}
                        </select>
                    </FilterField>

                    <FilterField label={t('common.startDate')}>
                        <input
                            type="date"
                            value={filters.fromDate}
                            onChange={(event) => handleFilterChange('fromDate', event.target.value)}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        />
                    </FilterField>

                    <FilterField label={t('common.endDate')}>
                        <input
                            type="date"
                            value={filters.toDate}
                            onChange={(event) => handleFilterChange('toDate', event.target.value)}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        />
                    </FilterField>

                    <FilterField label={t('orders.management.pageSize')}>
                        <select
                            value={String(filters.pageSize)}
                            onChange={(event) => handleFilterChange('pageSize', event.target.value)}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        >
                            {PAGE_SIZE_OPTIONS.map((option) => (
                                <option key={option} value={option}>
                                    {t('orders.management.rows', { count: option })}
                                </option>
                            ))}
                        </select>
                    </FilterField>

                    <FilterField label={t('orders.management.sortLabel')}>
                        <select
                            value={`${filters.sortBy}:${filters.sortDirection}`}
                            onChange={(event) => handleSortChange(event.target.value)}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-sky-400 focus:ring-2 focus:ring-sky-100"
                        >
                            {SORT_OPTIONS.map((option) => (
                                <option key={option.value} value={option.value}>
                                    {t(option.labelKey)}
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
                            <h2 className="text-lg font-semibold text-rose-900">{t('orders.management.loadFailed')}</h2>
                            <p className="mt-2 text-sm text-rose-700">
                                {orderListError?.message || t('orders.management.retryAfterRestore')}
                            </p>
                            {orderListError?.code || orderListError?.traceId ? (
                                <p className="mt-2 text-xs font-medium text-rose-700">
                                    {orderListError.code ? t('orders.management.code', { code: orderListError.code }) : null}
                                    {orderListError.code && orderListError.traceId ? ' | ' : null}
                                    {orderListError.traceId ? t('orders.management.trace', { traceId: orderListError.traceId }) : null}
                                </p>
                            ) : null}
                            <button
                                type="button"
                                onClick={() => ordersQuery.refetch()}
                                disabled={ordersQuery.isFetching}
                                className="mt-4 inline-flex items-center gap-2 rounded-full border border-rose-200 bg-white px-4 py-2 text-sm font-semibold text-rose-700 transition hover:border-rose-300 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                <RefreshCw className={`h-4 w-4 ${ordersQuery.isFetching ? 'animate-spin' : ''}`} />
                                {t('common.retry')}
                            </button>
                        </div>
                    </div>
                </section>
            ) : (
                <section className="rounded-[32px] border border-slate-200 bg-white shadow-sm">
                    <div className="flex flex-col gap-3 border-b border-slate-200 px-6 py-5 sm:flex-row sm:items-center sm:justify-between">
                        <div>
                            <p className="text-sm font-semibold text-slate-950">{t('orders.management.orderCount', { count: totalCount.toLocaleString(locale) })}</p>
                            <p className="mt-1 text-sm text-slate-500">
                                {t('orders.management.listScope')}
                            </p>
                            {ordersQuery.isFetching && !ordersQuery.isLoading ? (
                                <p className="mt-1 text-xs font-medium text-sky-700" role="status">{t('orders.management.refreshing')}</p>
                            ) : null}
                        </div>
                        {selectedOrderId ? (
                            <Link
                                to={`/ecommerce/orders${location.search}`}
                                className="inline-flex items-center gap-2 text-sm font-semibold text-sky-700 hover:text-sky-900"
                            >
                                {t('orders.management.clearSelected')}
                            </Link>
                        ) : null}
                    </div>

                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-slate-200" aria-busy={ordersQuery.isFetching}>
                            <caption className="sr-only">{t('orders.management.tableCaption')}</caption>
                            <thead className="bg-slate-50">
                                <tr className="text-start text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                    <th scope="col" className="px-6 py-4">{t('orders.management.order')}</th>
                                    <th scope="col" className="px-6 py-4">{t('orders.management.customer')}</th>
                                    <th scope="col" className="px-6 py-4">{t('orders.management.source')}</th>
                                    <th scope="col" className="px-6 py-4">{t('orders.management.total')}</th>
                                    <th scope="col" className="px-6 py-4">{t('orders.management.orderStatus')}</th>
                                    <th scope="col" className="px-6 py-4">{t('orders.management.payment')}</th>
                                    <th scope="col" className="px-6 py-4">{t('orders.management.created')}</th>
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
                                            {t('orders.management.empty')}
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
                                                    aria-label={t('orders.management.openOrder', { orderNumber: order.orderNumber })}
                                                >
                                                    {order.orderNumber}
                                                </button>
                                                <div className="mt-1 text-xs text-slate-500">{order.orderId}</div>
                                            </td>
                                            <td className="px-6 py-5 text-sm text-slate-700">{order.customerName || t('orders.management.guestCheckout')}</td>
                                            <td className="px-6 py-5">
                                                <div className="flex flex-col gap-2">
                                                    <StatusBadge
                                                        value={sourcePresentation.label}
                                                        tone={sourcePresentation.tone}
                                                    />
                                                    {affiliateAttribution ? (
                                                        <StatusBadge
                                                            value={t('orders.management.affiliateCode', { code: affiliateAttribution.referralCode })}
                                                            tone="emerald"
                                                        />
                                                    ) : null}
                                                    {affiliateAttributionWarning ? (
                                                        <StatusBadge
                                                            value={t('orders.management.affiliateUnavailable')}
                                                            tone="amber"
                                                        />
                                                    ) : null}
                                                </div>
                                            </td>
                                            <td className="px-6 py-5 text-sm font-semibold text-slate-950">
                                                {formatCurrency(order.totalAmount, order.currency, locale)}
                                            </td>
                                            <td className="px-6 py-5">
                                                <StatusBadge value={order.status} label={t(`orders.status.${order.status}`, { defaultValue: order.status })} tone={resolveOrderTone(order.status)} />
                                            </td>
                                            <td className="px-6 py-5">
                                                <StatusBadge value={order.paymentStatus} label={t(`orders.payment.${order.paymentStatus}`, { defaultValue: order.paymentStatus })} tone={resolvePaymentTone(order.paymentStatus)} />
                                            </td>
                                            <td className="px-6 py-5 text-sm text-slate-600">{formatDateTime(order.createdAtUtc, locale, t('common.unavailable'))}</td>
                                        </tr>
                                        )
                                    })
                                    : null}
                            </tbody>
                        </table>
                    </div>

                    <div className="flex flex-col gap-3 border-t border-slate-200 px-6 py-4 sm:flex-row sm:items-center sm:justify-between">
                        <p className="text-sm text-slate-500">
                            {t('orders.management.pageSummary', { page: currentPage, pages: Math.max(totalPages, 1) })}
                        </p>
                        <div className="flex items-center gap-2">
                            <button
                                type="button"
                                onClick={() => handlePageChange(Math.max(1, currentPage - 1))}
                                disabled={currentPage <= 1 || ordersQuery.isFetching}
                                className="rounded-full border border-slate-200 px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:text-slate-950 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                {t('common.previous')}
                            </button>
                            <button
                                type="button"
                                onClick={() => handlePageChange(currentPage + 1)}
                                disabled={!admittedOrderList?.hasNextPage || ordersQuery.isFetching}
                                className="rounded-full border border-slate-200 px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:text-slate-950 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                {t('common.next')}
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
                            customerTrustQuery.refetch()
                            sessionIntelligenceQuery.refetch()
                        }}
                        isRetrying={detailsQuery.isFetching || fulfillmentAuthorityQuery.isFetching || paymentAuthorityQuery.isFetching || customerTrustQuery.isFetching || sessionIntelligenceQuery.isFetching}
                        onStatusChange={(candidate) => statusMutation.mutate(candidate)}
                        isStatusUpdating={statusMutation.isPending}
                        statusUpdateError={statusMutation.error ? getLifecycleMutationErrorMessage(statusMutation.error, t) : null}
                        fulfillmentAuthority={fulfillmentAuthorityQuery.error ? null : fulfillmentAuthorityQuery.data}
                        fulfillmentAuthorityError={fulfillmentAuthorityQuery.error?.message || null}
                        isFulfillmentAuthorityLoading={fulfillmentAuthorityQuery.isLoading}
                        onFulfillmentAction={(actionCode) => fulfillmentActionMutation.mutate(actionCode)}
                        isFulfillmentActionUpdating={fulfillmentActionMutation.isPending}
                        fulfillmentActionError={fulfillmentActionMutation.error ? getLifecycleMutationErrorMessage(fulfillmentActionMutation.error, t) : null}
                        paymentAuthority={paymentAuthorityQuery.error ? null : paymentAuthorityQuery.data}
                        paymentAuthorityError={paymentAuthorityQuery.error?.message || null}
                        isPaymentAuthorityLoading={paymentAuthorityQuery.isLoading}
                        onPaymentAction={(action) => paymentActionMutation.mutate(action)}
                        isPaymentActionUpdating={paymentActionMutation.isPending}
                        paymentActionError={paymentActionMutation.error ? getLifecycleMutationErrorMessage(paymentActionMutation.error, t) : null}
                        onAdministrativeAction={(action) => administrativeActionMutation.mutate(action)}
                        isAdministrativeActionUpdating={administrativeActionMutation.isPending}
                        administrativeActionError={administrativeActionMutation.error ? getLifecycleMutationErrorMessage(administrativeActionMutation.error, t) : null}
                        administrativeActionResult={administrativeActionMutation.data?.outcome && administrativeActionMutation.data.shipmentId === selectedOrder?.shipment?.shipmentId ? administrativeActionMutation.data : null}
                        onConfirmReturnReceipt={(request) => returnReceiptMutation.mutate(request)}
                        isReturnReceiptUpdating={returnReceiptMutation.isPending}
                        returnReceiptError={returnReceiptMutation.error ? getLifecycleMutationErrorMessage(returnReceiptMutation.error, t) : null}
                        customerTrust={customerTrustQuery.data}
                        isCustomerTrustLoading={customerTrustQuery.isFetching}
                        customerTrustError={customerTrustQuery.error?.message || null}
                        onRetryCustomerTrust={() => customerTrustQuery.refetch()}
                        onCustomerPhoneAction={handleCustomerPhoneAction}
                        isCustomerPhoneActionUpdating={customerPhoneActionMutation.isPending}
                        customerPhoneActionError={customerPhoneActionMutation.error?.message || null}
                        sessionIntelligence={sessionIntelligenceQuery.data}
                        isSessionIntelligenceLoading={sessionIntelligenceQuery.isLoading}
                        sessionIntelligenceError={sessionIntelligenceQuery.error?.message || null}
                        onRetrySessionIntelligence={() => sessionIntelligenceQuery.refetch()}
                    />
                </>
            ) : null}
        </div>
    )
}

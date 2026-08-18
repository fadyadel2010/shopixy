import React, { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { Check, Eye, Image as ImageIcon, X } from 'lucide-react'
import api, { isRequestCanceled, isRequestTimeout } from '../services/api'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import { useToast } from '../components/Toast'
import { useRTL } from '../hooks/useRTL'
import { formatDateTime } from '../utils/dateFormat'
import { buildIdempotencyHeaders } from '../utils/requestSafety'

export const MANUAL_PAYMENT_REJECTION_REASON_MIN_LENGTH = 3
export const MANUAL_PAYMENT_REJECTION_REASON_MAX_LENGTH = 500

function getApiErrorMessage(error, fallback) {
    return error?.response?.data?.message || fallback
}

export function isManualPaymentConfigurationError(error) {
    const code = error?.response?.data?.code || error?.response?.data?.error
    return code === 'MANUAL_PAYMENT_CONFIGURATION_INVALID'
        || code === 'MANUAL_PAYMENT_CONFIGURATION_MISSING'
        || code === 'MANUAL_PAYMENT_PROVIDER_DISABLED'
}

function buildManualPaymentActionId(action, paymentIntentId) {
    return `manual-payment-${action}-${paymentIntentId}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
}

export function getManualPaymentActionErrorMessage(error, fallback, timeoutFallback) {
    if (isRequestTimeout(error)) {
        return timeoutFallback
    }

    return getApiErrorMessage(error, fallback)
}

export function getRejectionReasonValidationMessage(reason, t) {
    const trimmedReason = reason.trim()

    if (trimmedReason.length < MANUAL_PAYMENT_REJECTION_REASON_MIN_LENGTH) {
        return t(
            'ManualPayments.messages.rejectionReasonTooShort',
            'Rejection reason must be at least 3 characters.',
        )
    }

    if (trimmedReason.length > MANUAL_PAYMENT_REJECTION_REASON_MAX_LENGTH) {
        return t(
            'ManualPayments.messages.rejectionReasonTooLong',
            'Rejection reason must be 500 characters or fewer.',
        )
    }

    return ''
}

export default function ManualPayments() {
    const { t } = useTranslation()
    const { dir, isRTL, locale } = useRTL()
    const { confirm } = useConfirmDialog()
    const { showError, showSuccess } = useToast()

    const [payments, setPayments] = useState([])
    const [loading, setLoading] = useState(true)
    const [listError, setListError] = useState('')
    const [listErrorType, setListErrorType] = useState('')
    const [selectedProof, setSelectedProof] = useState(null)
    const [proofImageUrl, setProofImageUrl] = useState('')
    const [proofMimeType, setProofMimeType] = useState('')
    const [proofLoading, setProofLoading] = useState(false)
    const [showRejectModal, setShowRejectModal] = useState(false)
    const [selectedPaymentId, setSelectedPaymentId] = useState(null)
    const [rejectionReason, setRejectionReason] = useState('')
    const [pendingPaymentId, setPendingPaymentId] = useState(null)
    const [pagination, setPagination] = useState({
        page: 1,
        pageSize: 25,
        totalCount: 0,
        totalPages: 0,
        hasMore: false,
    })

    const pendingPaymentIdRef = useRef(null)
    const isMountedRef = useRef(true)
    const paymentsAbortCtrlRef = useRef(null)
    const selectedPaymentIdRef = useRef(null)
    const proofObjectUrlRef = useRef(null)
    const actionRequestIdsRef = useRef(new Map())
    const isSubmitting = pendingPaymentId !== null
    const rejectionReasonLength = rejectionReason.trim().length
    const rejectionReasonValidationMessage = showRejectModal && rejectionReason
        ? getRejectionReasonValidationMessage(rejectionReason, t)
        : ''
    const isRejectSubmitDisabled = isSubmitting
        || rejectionReasonLength < MANUAL_PAYMENT_REJECTION_REASON_MIN_LENGTH
        || rejectionReasonLength > MANUAL_PAYMENT_REJECTION_REASON_MAX_LENGTH

    const getManualPaymentActionHeaders = (action, paymentIntentId) => {
        const cacheKey = `${action}:${paymentIntentId}`
        let actionRequestId = actionRequestIdsRef.current.get(cacheKey)

        if (!actionRequestId) {
            actionRequestId = buildManualPaymentActionId(action, paymentIntentId)
            actionRequestIdsRef.current.set(cacheKey, actionRequestId)
        }

        return buildIdempotencyHeaders(actionRequestId)
    }

    const clearManualPaymentActionHeaders = (action, paymentIntentId) => {
        actionRequestIdsRef.current.delete(`${action}:${paymentIntentId}`)
    }

    const resetPaymentsAbortController = () => {
        if (!isMountedRef.current) {
            return null
        }

        paymentsAbortCtrlRef.current?.abort()
        paymentsAbortCtrlRef.current = new AbortController()
        return paymentsAbortCtrlRef.current
    }

    const fetchPayments = async (signal, nextPage = pagination.page, nextPageSize = pagination.pageSize) => {
        try {
            setLoading(true)
            setListError('')
            setListErrorType('')
            const response = await api.get('/admin/payments/manual-wallet/pending', {
                params: {
                    page: nextPage,
                    pageSize: nextPageSize,
                },
                signal,
            })

            if (signal?.aborted) {
                return
            }

            // Clamp page to valid range to prevent empty page after deletions
            const totalPages = response.data.totalPages || 0
            const clampedPage = Math.max(1, Math.min(response.data.page || nextPage, totalPages || 1))
            
            setPayments(response.data.items || [])
            setPagination({
                page: clampedPage,
                pageSize: response.data.pageSize || nextPageSize,
                totalCount: response.data.totalCount || 0,
                totalPages: totalPages,
                hasMore: Boolean(response.data.hasMore),
            })

            // Note: If clampedPage differs from the requested page, setPagination above
            // will trigger the useEffect to refetch with the correct page automatically.
            // No need for a recursive fetch here to avoid race conditions.
        } catch (error) {
            if (isRequestCanceled(error)) {
                return
            }

            console.error('Error fetching payments:', error)
            const configurationError = isManualPaymentConfigurationError(error)
            const message = configurationError
                ? t('ManualPayments.messages.configurationRequired', 'Configuration required.')
                : getApiErrorMessage(error, t('ManualPayments.messages.loadError'))
            setPayments([])
            setListError(message)
            setListErrorType(configurationError ? 'configuration' : 'failure')
            showError(message)
        } finally {
            if (!signal?.aborted) {
                setLoading(false)
            }
        }
    }

    useEffect(() => {
        isMountedRef.current = true
        const controller = resetPaymentsAbortController()
        if (controller) {
            void fetchPayments(controller.signal, pagination.page, pagination.pageSize)
        }

        return () => {
            isMountedRef.current = false
            paymentsAbortCtrlRef.current?.abort()
            paymentsAbortCtrlRef.current = null
        }
    }, [pagination.page, pagination.pageSize, t])

    useEffect(() => {
        selectedPaymentIdRef.current = selectedPaymentId
    }, [selectedPaymentId])

    useEffect(() => {
        if (!selectedProof) {
            setProofLoading(false)
            setProofImageUrl('')
            setProofMimeType('')

            if (proofObjectUrlRef.current) {
                URL.revokeObjectURL(proofObjectUrlRef.current)
                proofObjectUrlRef.current = null
            }

            return undefined
        }

        setProofLoading(true)
        setProofImageUrl('')
        setProofMimeType('')

        if (proofObjectUrlRef.current) {
            URL.revokeObjectURL(proofObjectUrlRef.current)
            proofObjectUrlRef.current = null
        }

        const controller = new AbortController()

        const loadProofImage = async () => {
            try {
                const response = await api.get(
                    `/admin/payments/manual-wallet/proof-image/${selectedProof.paymentIntentId}`,
                    {
                        responseType: 'blob',
                        signal: controller.signal,
                    },
                )

                if (controller.signal.aborted) {
                    return
                }

                const objectUrl = URL.createObjectURL(response.data)
                proofObjectUrlRef.current = objectUrl
                setProofImageUrl(objectUrl)
                setProofMimeType(response.data.type || '')
            } catch (error) {
                if (isRequestCanceled(error)) {
                    return
                }

                console.error('Error loading proof image:', error)
                showError(getApiErrorMessage(
                    error,
                    t('ManualPayments.messages.proofLoadError', 'Failed to load payment proof'),
                ))
            } finally {
                if (!controller.signal.aborted) {
                    setProofLoading(false)
                }
            }
        }

        void loadProofImage()

        return () => {
            controller.abort()
            setProofImageUrl('')
            setProofMimeType('')
            setProofLoading(false)

            if (proofObjectUrlRef.current) {
                URL.revokeObjectURL(proofObjectUrlRef.current)
                proofObjectUrlRef.current = null
            }
        }
    }, [selectedProof, showError, t])

    const refreshPayments = async () => {
        const controller = resetPaymentsAbortController()
        if (controller) {
            await fetchPayments(controller.signal, pagination.page, pagination.pageSize)
        }
    }

    const handleApprove = async (paymentIntentId) => {
        if (pendingPaymentIdRef.current) {
            return
        }

        const confirmed = await confirm({
            confirmLabel: t('common.confirm'),
            description: t('ManualPayments.messages.approveConfirm'),
            title: t('common.confirm'),
            variant: 'warning',
        })

        if (!confirmed || pendingPaymentIdRef.current) {
            return
        }

        pendingPaymentIdRef.current = paymentIntentId
        setPendingPaymentId(paymentIntentId)

        try {
            await api.post(
                `/admin/payments/manual-wallet/${paymentIntentId}/confirm`,
                {},
                {
                    headers: getManualPaymentActionHeaders('confirm', paymentIntentId),
                },
            )
            clearManualPaymentActionHeaders('confirm', paymentIntentId)
            showSuccess(t('ManualPayments.messages.approveSuccess'))
        } catch (error) {
            console.error('Error approving payment:', error)
            if (error?.response) {
                clearManualPaymentActionHeaders('confirm', paymentIntentId)
            }
            showError(getManualPaymentActionErrorMessage(
                error,
                t('ManualPayments.messages.approveError'),
                t(
                    'ManualPayments.messages.approveTimeout',
                    'Approval request timed out. Refresh the pending list before trying again.',
                ),
            ))
        } finally {
            await refreshPayments()
            pendingPaymentIdRef.current = null
            setPendingPaymentId(null)
        }
    }

    const handleReject = async () => {
        if (pendingPaymentIdRef.current) {
            return
        }

        const trimmedReason = rejectionReason.trim()
        const validationMessage = getRejectionReasonValidationMessage(trimmedReason, t)

        if (validationMessage) {
            showError(validationMessage)
            return
        }

        const originPaymentId = selectedPaymentIdRef.current
        if (!originPaymentId) {
            return
        }

        pendingPaymentIdRef.current = originPaymentId
        setPendingPaymentId(originPaymentId)

        try {
            await api.post(
                `/admin/payments/manual-wallet/${originPaymentId}/reject`,
                {
                    reason: trimmedReason,
                },
                {
                    headers: getManualPaymentActionHeaders('reject', originPaymentId),
                },
            )
            clearManualPaymentActionHeaders('reject', originPaymentId)
            showSuccess(t('ManualPayments.messages.rejectSuccess'))
            setShowRejectModal(false)
            setRejectionReason('')
        } catch (error) {
            console.error('Error rejecting payment:', error)
            if (error?.response) {
                clearManualPaymentActionHeaders('reject', originPaymentId)
            }
            showError(getManualPaymentActionErrorMessage(
                error,
                t('ManualPayments.messages.rejectError'),
                t(
                    'ManualPayments.messages.rejectTimeout',
                    'Rejection request timed out. Refresh the pending list before trying again.',
                ),
            ))
        } finally {
            await refreshPayments()
            pendingPaymentIdRef.current = null
            setPendingPaymentId(null)
        }
    }

    const renderProofContent = () => {
        if (proofLoading) {
            return (
                <div className="flex min-h-[16rem] min-w-[16rem] items-center justify-center p-10 text-sm text-gray-200">
                    {t('common.loading')}
                </div>
            )
        }

        if (!proofImageUrl) {
            return (
                <div className="flex min-h-[16rem] min-w-[16rem] items-center justify-center p-10 text-sm text-gray-200">
                    {t('ManualPayments.messages.proofLoadError', 'Failed to load payment proof')}
                </div>
            )
        }

        if (proofMimeType === 'application/pdf') {
            return (
                <iframe
                    src={proofImageUrl}
                    title={t('ManualPayments.proofOfPayment')}
                    className="h-[75vh] w-[min(90vw,56rem)] bg-white"
                />
            )
        }

        return (
            <img
                src={proofImageUrl}
                alt={t('ManualPayments.proofOfPayment')}
                className="max-w-full h-auto block"
            />
        )
    }

    return (
        <div dir={dir} className="p-6 space-y-4">
            <div className="flex items-center justify-between gap-4">
                <h1 className="text-2xl font-bold text-gray-800">{t('ManualPayments.pending_manual_payments')}</h1>
                <button
                    type="button"
                    onClick={() => void refreshPayments()}
                    className="rounded-lg border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
                >
                    {t('common.refresh', 'Refresh')}
                </button>
            </div>

            {listError && (
                <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700" role="alert">
                    {listError}
                </div>
            )}

            <div className="bg-white rounded-xl shadow-sm border border-gray-100 overflow-hidden">
                <div className="overflow-x-auto">
                    <table className={`w-full ${isRTL ? 'text-right' : 'text-left'}`}>
                        <thead>
                            <tr className="bg-gray-50 text-gray-500 text-sm border-b border-gray-100">
                                <th className="p-4 font-medium">{t('ManualPayments.order')}</th>
                                <th className="p-4 font-medium">{t('ManualPayments.customer')}</th>
                                <th className="p-4 font-medium">{t('ManualPayments.amount')}</th>
                                <th className="p-4 font-medium">{t('ManualPayments.wallet_info')}</th>
                                <th className="p-4 font-medium">{t('ManualPayments.date')}</th>
                                <th className="p-4 font-medium">{t('ManualPayments.proof')}</th>
                                <th className="p-4 font-medium text-right">{t('ManualPayments.actionsHeader')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {loading ? (
                                <tr><td colSpan="7" className="p-8 text-center text-gray-500">{t('ManualPayments.loading')}</td></tr>
                            ) : listError ? (
                                <tr>
                                    <td colSpan="7" className="p-8 text-center text-gray-500">
                                        {listErrorType === 'configuration'
                                            ? t('ManualPayments.messages.configurationRequired', 'Configuration required.')
                                            : t('ManualPayments.messages.failedToLoad', 'Failed to load pending payments.')}
                                    </td>
                                </tr>
                            ) : payments.length === 0 ? (
                                <tr><td colSpan="7" className="p-8 text-center text-gray-500">{t('ManualPayments.no_pending_manual_payments_found')}</td></tr>
                            ) : (
                                payments.map((payment) => (
                                    <tr key={payment.paymentIntentId} className="hover:bg-gray-50 transition-colors">
                                        <td className="p-4">
                                            <Link
                                                to={`/ecommerce/orders/${payment.orderId}`}
                                                className="font-medium text-blue-600 hover:text-blue-800 hover:underline"
                                                title={t('ManualPayments.actions.viewOrderDetails')}
                                            >
                                                {payment.orderNumber}
                                            </Link>
                                        </td>
                                        <td className="p-4 text-gray-700">{payment.customerName}</td>
                                        <td className="p-4 font-medium text-gray-900">
                                            {new Intl.NumberFormat('en-US', { style: 'currency', currency: payment.currency }).format(payment.amount)}
                                        </td>
                                        <td className="p-4 text-sm">
                                            <div className="font-medium text-gray-800">{payment.walletType || t('ManualPayments.notAvailable')}</div>
                                            <div className="text-gray-500">{payment.walletPhoneNumber || t('ManualPayments.notAvailable')}</div>
                                        </td>
                                        <td className="p-4 text-gray-500 text-sm">
                                            {formatDateTime(payment.createdAtUtc, locale)}
                                        </td>
                                        <td className="p-4">
                                            {payment.hasProofFile ? (
                                                <button
                                                    type="button"
                                                    onClick={() => setSelectedProof(payment)}
                                                    className="flex items-center gap-2 text-sm text-blue-600 hover:text-blue-800"
                                                    aria-label={t('ManualPayments.proofOfPayment')}
                                                >
                                                    <ImageIcon size={16} />
                                                    {t('ManualPayments.view')}
                                                </button>
                                            ) : (
                                                <span className="text-gray-400 italic text-sm">{t('ManualPayments.no_image')}</span>
                                            )}
                                        </td>
                                        <td className={`p-4 ${isRTL ? 'text-left' : 'text-right'}`}>
                                            <div className={`flex items-center gap-2 ${isRTL ? 'justify-start' : 'justify-end'}`}>
                                                <Link
                                                    to={`/ecommerce/orders/${payment.orderId}`}
                                                    className="p-2 text-blue-600 hover:bg-blue-50 rounded-full transition-colors"
                                                    title={t('ManualPayments.actions.viewOrderDetails')}
                                                    aria-label={t('ManualPayments.actions.viewOrderDetails')}
                                                >
                                                    <Eye size={20} />
                                                </Link>
                                                <button
                                                    type="button"
                                                    onClick={() => void handleApprove(payment.paymentIntentId)}
                                                    disabled={isSubmitting}
                                                    className="p-2 text-green-600 hover:bg-green-50 rounded-full transition-colors disabled:cursor-not-allowed disabled:opacity-50"
                                                    title={t('ManualPayments.actions.approve')}
                                                    aria-label={t('ManualPayments.actions.approve')}
                                                >
                                                    <Check size={20} />
                                                </button>
                                                <button
                                                    type="button"
                                                    onClick={() => {
                                                        setSelectedPaymentId(payment.paymentIntentId)
                                                        setShowRejectModal(true)
                                                    }}
                                                    disabled={isSubmitting}
                                                    className="p-2 text-red-600 hover:bg-red-50 rounded-full transition-colors disabled:cursor-not-allowed disabled:opacity-50"
                                                    title={t('ManualPayments.actions.reject')}
                                                    aria-label={t('ManualPayments.actions.reject')}
                                                >
                                                    <X size={20} />
                                                </button>
                                            </div>
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>

                {pagination.totalPages > 1 && (
                    <div className="flex items-center justify-between px-4 py-3 border-t border-gray-100 bg-gray-50">
                        <p className="text-sm text-gray-600">
                            {((pagination.page - 1) * pagination.pageSize) + 1}-{Math.min(pagination.page * pagination.pageSize, pagination.totalCount)} / {pagination.totalCount}
                        </p>
                        <div className="flex items-center gap-2">
                            <button
                                type="button"
                                onClick={() => setPagination((previous) => ({ ...previous, page: previous.page - 1 }))}
                                disabled={pagination.page <= 1 || loading}
                                className="rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                {t('common.previous', 'Previous')}
                            </button>
                            <span className="text-sm text-gray-600">
                                {pagination.page} / {pagination.totalPages}
                            </span>
                            <button
                                type="button"
                                onClick={() => setPagination((previous) => ({ ...previous, page: previous.page + 1 }))}
                                disabled={!pagination.hasMore || loading}
                                className="rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                {t('common.next', 'Next')}
                            </button>
                        </div>
                    </div>
                )}
            </div>

            {selectedProof && (
                <div className="fixed inset-0 bg-black/80 z-50 flex items-center justify-center p-4 text-white" onClick={() => setSelectedProof(null)}>
                    <div className="relative max-w-5xl max-h-[90vh] overflow-auto rounded-lg bg-gray-900" onClick={(event) => event.stopPropagation()}>
                        {renderProofContent()}
                        <button
                            type="button"
                            onClick={() => setSelectedProof(null)}
                            className={`absolute top-4 bg-black/50 p-2 rounded-full hover:bg-black/70 ${isRTL ? 'left-4' : 'right-4'}`}
                            aria-label={t('common.close')}
                        >
                            <X size={24} />
                        </button>
                    </div>
                </div>
            )}

            {showRejectModal && (
                <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
                    <div className="bg-white rounded-xl shadow-xl max-w-md w-full p-6" onClick={(event) => event.stopPropagation()}>
                        <h2 className="text-xl font-bold mb-4 text-gray-800">{t('ManualPayments.reject_payment')}</h2>
                        <label
                            htmlFor="manual-payment-rejection-reason"
                            className="block text-sm font-medium text-gray-700 mb-2"
                        >
                            {t('ManualPayments.rejectionReasonLabel', 'Rejection Reason')}
                            <span className="text-red-600 ml-1" aria-label="required">*</span>
                        </label>
                        <textarea
                            id="manual-payment-rejection-reason"
                            className="w-full border border-gray-300 rounded-lg p-3 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 outline-none"
                            placeholder={t('ManualPayments.rejectionReasonPlaceholder')}
                            rows="4"
                            value={rejectionReason}
                            onChange={(event) => setRejectionReason(event.target.value)}
                            disabled={isSubmitting}
                            maxLength={MANUAL_PAYMENT_REJECTION_REASON_MAX_LENGTH}
                            aria-invalid={Boolean(rejectionReasonValidationMessage)}
                            aria-describedby="manual-payment-rejection-reason-help"
                            aria-required="true"
                        />
                        <div
                            id="manual-payment-rejection-reason-help"
                            className={`mt-2 text-sm ${rejectionReasonValidationMessage ? 'text-red-600' : 'text-gray-500'}`}
                            role={rejectionReasonValidationMessage ? 'alert' : undefined}
                        >
                            {rejectionReasonValidationMessage || `${rejectionReasonLength}/${MANUAL_PAYMENT_REJECTION_REASON_MAX_LENGTH}`}
                        </div>
                        <div className={`flex gap-3 mt-6 ${isRTL ? 'justify-start' : 'justify-end'}`}>
                            <button
                                type="button"
                                disabled={isSubmitting}
                                onClick={() => {
                                    setShowRejectModal(false)
                                    setRejectionReason('')
                                }}
                                className="px-4 py-2 text-gray-600 hover:bg-gray-100 rounded-lg font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                {t('ManualPayments.cancel')}
                            </button>
                            <button
                                type="button"
                                disabled={isRejectSubmitDisabled}
                                onClick={() => void handleReject()}
                                className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                {t('ManualPayments.reject_payment')}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    )
}

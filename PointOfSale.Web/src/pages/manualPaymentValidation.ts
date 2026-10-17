import { isRequestTimeout } from '../services/api'

export const MANUAL_PAYMENT_REJECTION_REASON_MIN_LENGTH = 3
export const MANUAL_PAYMENT_REJECTION_REASON_MAX_LENGTH = 500

const failureData = (error: unknown): Record<string, unknown> | null => {
    if (typeof error !== 'object' || error === null || !('response' in error)) return null
    const response = error.response
    if (typeof response !== 'object' || response === null || !('data' in response)) return null
    return typeof response.data === 'object' && response.data !== null ? { ...response.data } : null
}

export function isManualPaymentConfigurationError(error: unknown) {
    const data = failureData(error)
    const code = data?.code || data?.error
    return code === 'MANUAL_PAYMENT_CONFIGURATION_INVALID'
        || code === 'MANUAL_PAYMENT_CONFIGURATION_MISSING'
        || code === 'MANUAL_PAYMENT_PROVIDER_DISABLED'
}

export function getManualPaymentActionErrorMessage(error: unknown, fallback: string, timeoutFallback: string): string {
    if (isRequestTimeout(error)) return timeoutFallback
    const message = failureData(error)?.message
    return typeof message === 'string' && message ? message : fallback
}

export function getRejectionReasonValidationMessage(reason: string, t: (key: string, fallback?: string) => string) {
    const trimmedReason = reason.trim()
    if (trimmedReason.length < MANUAL_PAYMENT_REJECTION_REASON_MIN_LENGTH) {
        return t('ManualPayments.messages.rejectionReasonTooShort', 'Rejection reason must be at least 3 characters.')
    }
    if (trimmedReason.length > MANUAL_PAYMENT_REJECTION_REASON_MAX_LENGTH) {
        return t('ManualPayments.messages.rejectionReasonTooLong', 'Rejection reason must be 500 characters or fewer.')
    }
    return ''
}

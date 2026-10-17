import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import {
    MANUAL_PAYMENT_REJECTION_REASON_MAX_LENGTH,
    MANUAL_PAYMENT_REJECTION_REASON_MIN_LENGTH,
    getManualPaymentActionErrorMessage,
    getRejectionReasonValidationMessage,
    isManualPaymentConfigurationError,
} from './manualPaymentValidation'

const currentDir = dirname(fileURLToPath(import.meta.url))
const manualPaymentsPath = join(currentDir, 'ManualPayments.jsx')

describe('ManualPayments mutation safety', () => {
    it('uses stable idempotency headers for approve and reject admin actions', () => {
        const source = readFileSync(manualPaymentsPath, 'utf8')

        expect(source).toContain("import { buildIdempotencyHeaders } from '../utils/requestSafety'")
        expect(source).toContain('const actionRequestIdsRef = useRef(new Map())')
        expect(source).toContain("headers: getManualPaymentActionHeaders('confirm', paymentIntentId)")
        expect(source).toContain("headers: getManualPaymentActionHeaders('reject', originPaymentId)")
        expect(source).toContain("clearManualPaymentActionHeaders('confirm', paymentIntentId)")
        expect(source).toContain("clearManualPaymentActionHeaders('reject', originPaymentId)")
    })

    it('keeps timeout errors explicit so admins refresh before retrying', () => {
        const message = getManualPaymentActionErrorMessage(
            { code: 'ECONNABORTED' },
            'Generic failure',
            'Approval request timed out. Refresh the pending list before trying again.',
        )

        expect(message).toBe('Approval request timed out. Refresh the pending list before trying again.')
    })

    it('classifies manual payment configuration failures separately from an empty queue', () => {
        expect(isManualPaymentConfigurationError({
            response: {
                data: {
                    code: 'MANUAL_PAYMENT_CONFIGURATION_INVALID',
                },
            },
        })).toBe(true)

        expect(isManualPaymentConfigurationError({
            response: {
                data: {
                    code: 'INTERNAL_ERROR',
                },
            },
        })).toBe(false)
    })

    it('still surfaces backend validation messages for rejected admin actions', () => {
        const message = getManualPaymentActionErrorMessage(
            {
                response: {
                    data: {
                        message: 'Payment is already rejected',
                    },
                },
            },
            'Generic failure',
            'Timeout',
        )

        expect(message).toBe('Payment is already rejected')
    })

    it('matches the backend rejection reason length contract', () => {
        const t = (_key: string, fallback?: string) => fallback ?? ''

        expect(MANUAL_PAYMENT_REJECTION_REASON_MIN_LENGTH).toBe(3)
        expect(MANUAL_PAYMENT_REJECTION_REASON_MAX_LENGTH).toBe(500)
        expect(getRejectionReasonValidationMessage('no', t)).toBe('Rejection reason must be at least 3 characters.')
        expect(getRejectionReasonValidationMessage('valid reason', t)).toBe('')
        expect(getRejectionReasonValidationMessage('x'.repeat(501), t)).toBe('Rejection reason must be 500 characters or fewer.')
    })

    it('wires rejection reason validation and icon-only buttons for assistive tech', () => {
        const source = readFileSync(manualPaymentsPath, 'utf8')

        expect(source).toContain('maxLength={MANUAL_PAYMENT_REJECTION_REASON_MAX_LENGTH}')
        expect(source).toContain('aria-invalid={Boolean(rejectionReasonValidationMessage)}')
        expect(source).toContain('aria-describedby="manual-payment-rejection-reason-help"')
        expect(source).toContain('id="manual-payment-rejection-reason-help"')
        expect(source).toContain('disabled={isRejectSubmitDisabled}')
        expect(source).toContain("aria-label={t('ManualPayments.actions.approve')}")
        expect(source).toContain("aria-label={t('ManualPayments.actions.reject')}")
        expect(source).toContain("aria-label={t('ManualPayments.actions.viewOrderDetails')}")
        
        // Verify accessible label for rejection reason textarea
        expect(source).toContain('htmlFor="manual-payment-rejection-reason"')
        expect(source).toContain('id="manual-payment-rejection-reason"')
        expect(source).toContain('aria-required="true"')
    })

    it('does not render empty queue copy when the pending list request failed', () => {
        const source = readFileSync(manualPaymentsPath, 'utf8')
        const failedLoadBranchIndex = source.indexOf(') : listError ? (')
        const emptyQueueBranchIndex = source.indexOf(') : payments.length === 0 ? (')

        expect(failedLoadBranchIndex).toBeGreaterThan(0)
        expect(emptyQueueBranchIndex).toBeGreaterThan(failedLoadBranchIndex)
        expect(source).toContain("t('ManualPayments.messages.configurationRequired', 'Configuration required.')")
        expect(source).toContain("t('ManualPayments.messages.failedToLoad', 'Failed to load pending payments.')")
    })

    it('classifies proof retrieval failures separately from pending-list failures', () => {
        const source = readFileSync(manualPaymentsPath, 'utf8')

        expect(source).toContain("t('ManualPayments.messages.proofLoadError', 'Failed to load payment proof')")
    })
})

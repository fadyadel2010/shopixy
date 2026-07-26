import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import i18n from 'i18next'
import PaymentModal, { getMixedPaymentState, getSaleSubmissionErrorMessage, parseAmountInput } from './PaymentModal.jsx'
import { SettingsProvider } from '../contexts/SettingsContext'

const apiMock = vi.hoisted(() => ({
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
}))

vi.mock('../services/api', () => ({
    default: apiMock,
    getSafeApiErrorMessage: (error: any) => error.message || error.response?.data?.message || 'Unknown error',
    isRequestTimeout: (error: any) => error.code === 'ECONNABORTED' || error.message?.includes('timeout'),
}))

// Initialize i18n for tests
i18n.init({
    lng: 'en',
    fallbackLng: 'en',
    resources: {
        en: {
            translation: {
                'paymentModal.title': 'Payment',
                'paymentModal.cashAmount': 'Cash Amount',
                'paymentModal.completeSale': 'Complete Sale',
                'paymentModal.confirmPayment': 'Confirm Payment',
                'paymentModal.confirmPaymentTitle': 'Confirm Payment',
                'paymentModal.confirmPaymentMessage': 'Please confirm the payment details',
                'paymentModal.totalAmount': 'Total Amount',
                'paymentModal.validation.cashAmountTooLow': 'Cash amount is too low',
                'common.cancel': 'Cancel',
                'common.close': 'Close',
            },
        },
    },
})

beforeEach(() => {
    apiMock.get.mockResolvedValue({
        data: {
            invoiceNumber: 'INV-001',
        },
    })
    apiMock.post.mockResolvedValue({
        data: {
            invoiceId: 'sale-123',
            invoiceNumber: 'INV-001',
        },
    })
    apiMock.put.mockResolvedValue({
        data: {
            invoiceId: 'sale-123',
            invoiceNumber: 'INV-001',
        },
    })
})

afterEach(() => {
    vi.useRealTimers()
    vi.clearAllMocks()
})

// Test wrapper with required providers
const TestWrapper = ({ children }) => (
    <I18nextProvider i18n={i18n}>
        <SettingsProvider>
            {children}
        </SettingsProvider>
    </I18nextProvider>
)

describe('PaymentModal amount parsing', () => {
    it('rejects blank amount input instead of treating it as zero', () => {
        expect(parseAmountInput('')).toMatchObject({ isValid: false, value: null, reason: 'required' })
        expect(parseAmountInput('   ')).toMatchObject({ isValid: false, value: null, reason: 'required' })
        expect(parseAmountInput(null)).toMatchObject({ isValid: false, value: null, reason: 'required' })
    })

    it('rejects unsafe financial formats instead of coercing them', () => {
        expect(parseAmountInput('1e3')).toMatchObject({ isValid: false, value: null, reason: 'invalid_format' })
        expect(parseAmountInput('10.125')).toMatchObject({ isValid: false, value: null, reason: 'too_many_decimals' })
        expect(parseAmountInput('-1')).toMatchObject({ isValid: false, value: null, reason: 'negative' })
        expect(parseAmountInput('1,000')).toMatchObject({ isValid: false, value: null, reason: 'invalid_format' })
    })

    it('accepts finite numeric amount input', () => {
        expect(parseAmountInput('25.50')).toMatchObject({ isValid: true, value: 25.5 })
        expect(parseAmountInput(10)).toMatchObject({ isValid: true, value: 10 })
    })
})

describe('PaymentModal mixed payment state', () => {
    it('marks blank mixed cash amount invalid even though card portion can cover the total', () => {
        const state = getMixedPaymentState(100, '')

        expect(state.hasInvalidCashAmount).toBe(true)
        expect(state.normalizedCashAmount).toBe(0)
        expect(state.normalizedCardAmount).toBe(100)
        expect(state.totalPaid).toBe(100)
    })

    it('derives the card remainder from an explicit two-decimal mixed cash amount', () => {
        const state = getMixedPaymentState(100, '25.12')

        expect(state.hasInvalidCashAmount).toBe(false)
        expect(state.normalizedCashAmount).toBe(25.12)
        expect(state.normalizedCardAmount).toBe(74.88)
        expect(state.totalPaid).toBe(100)
        expect(state.shouldClampCashAmount).toBe(false)
    })

    it('rejects mixed cash amounts that silently collapse to a single payment method', () => {
        expect(getMixedPaymentState(100, '0').hasZeroCashAmount).toBe(true)
        expect(getMixedPaymentState(100, '100').hasCashAmountAtOrAboveTotal).toBe(true)
    })
})

describe('PaymentModal submission concurrency', () => {
    it('uses a synchronous confirmation lock before stock revalidation starts', async () => {
        const user = userEvent.setup()
        let revalidateResolve
        const mockRevalidateStock = vi.fn(() => new Promise(resolve => {
            // Store resolve to control when promise completes
            revalidateResolve = resolve
        }))
        const mockOnComplete = vi.fn()
        const mockOnError = vi.fn()
        const mockOnClose = vi.fn()

        const items = [
            {
                productId: 'prod-1',
                variantId: null,
                unitId: null,
                name: 'Test Product',
                quantity: 1,
                unitPrice: 100,
                discountAmount: 0,
                taxRate: 0,
            },
        ]

        render(
            <PaymentModal
                total={100}
                items={items}
                customer={null}
                discountType="fixed"
                discountValue={0}
                warehouseId="warehouse-1"
                paymentAttemptId="attempt-1"
                onRevalidateStock={mockRevalidateStock}
                onClose={mockOnClose}
                onComplete={mockOnComplete}
                onError={mockOnError}
                editingSaleId={null}
            />,
            { wrapper: TestWrapper }
        )

        // Submit the payment to trigger confirmation modal
        const submitButton = screen.getByRole('button', { name: /complete sale/i })
        await user.click(submitButton)

        // Confirmation modal should appear
        const confirmButton = await screen.findByRole('button', { name: /confirm payment/i })
        expect(confirmButton).toBeInTheDocument()
        expect(confirmButton).toBeEnabled()

        // Click confirm - this should set confirmationLockRef.current = true BEFORE calling onRevalidateStock
        await user.click(confirmButton)

        // Verify stock revalidation was called (lock was set synchronously before this async call)
        await waitFor(() => {
            expect(mockRevalidateStock).toHaveBeenCalledTimes(1)
        })

        // While the promise is pending, the button should be disabled (lock is active)
        expect(confirmButton).toBeDisabled()

        // Resolve the stock revalidation
        revalidateResolve(true)

        // After completion, lock should be cleared
        await waitFor(() => {
            expect(mockRevalidateStock).toHaveBeenCalledTimes(1)
        })
    })

    it('keeps ambiguous timed-out submissions in the confirmation dialog for same-key retry', async () => {
        const user = userEvent.setup()
        const mockRevalidateStock = vi.fn(() => Promise.resolve(true))
        const mockOnComplete = vi.fn()
        const mockOnError = vi.fn()
        const mockOnClose = vi.fn()

        const items = [
            {
                productId: 'prod-1',
                variantId: null,
                unitId: null,
                name: 'Test Product',
                quantity: 1,
                unitPrice: 100,
                discountAmount: 0,
                taxRate: 0,
            },
        ]

        let rejectSubmission: (error: any) => void
        const submissionPromise = new Promise((_, reject) => {
            rejectSubmission = reject
        })
        apiMock.post.mockReturnValue(submissionPromise)

        render(
            <PaymentModal
                total={100}
                items={items}
                customer={null}
                discountType="fixed"
                discountValue={0}
                warehouseId="warehouse-1"
                paymentAttemptId="attempt-1"
                onRevalidateStock={mockRevalidateStock}
                onClose={mockOnClose}
                onComplete={mockOnComplete}
                onError={mockOnError}
                editingSaleId={null}
            />,
            { wrapper: TestWrapper }
        )

        // Submit the payment to trigger confirmation modal
        const submitButton = screen.getByRole('button', { name: /complete sale/i })
        await user.click(submitButton)

        // Confirmation modal should appear
        const confirmButton = await screen.findByRole('button', { name: /confirm payment/i })
        expect(confirmButton).toBeInTheDocument()

        // Click confirm to start the submission
        await user.click(confirmButton)

        // Simulate timeout error after advancing timers
        rejectSubmission!({
            code: 'ECONNABORTED',
            message: 'Request timeout',
        })

        // Wait for the error to be processed
        await waitFor(() => {
            expect(mockOnError).toHaveBeenCalled()
        })

        // Confirmation dialog should remain open (not closed due to ambiguous state)
        expect(screen.getByRole('button', { name: /confirm payment/i })).toBeInTheDocument()

        // Cancel button should be disabled during ambiguous state
        const cancelButtons = screen.getAllByRole('button', { name: /cancel/i })
        const confirmModalCancelButton = cancelButtons[cancelButtons.length - 1]
        expect(confirmModalCancelButton).toBeDisabled()

        // Close button should be hidden (hideCloseButton is true during ambiguous state)
        expect(screen.queryByLabelText(/close/i)).not.toBeInTheDocument()

        // Mock successful retry
        const retryPromise = Promise.resolve({
            data: {
                orderId: 'order-123',
                invoiceNumber: 'INV-001',
            },
        })
        apiMock.post.mockReturnValue(retryPromise)

        // Retry by clicking confirm again
        const retryConfirmButton = screen.getByRole('button', { name: /confirm payment/i })
        await user.click(retryConfirmButton)

        // Wait for successful completion
        await waitFor(() => {
            expect(mockOnComplete).toHaveBeenCalledWith(
                expect.objectContaining({
                    orderId: 'order-123',
                    attemptId: 'attempt-1',
                })
            )
        })

        // Verify revalidation was called
        expect(mockRevalidateStock).toHaveBeenCalled()
    })

    it('keeps active edit conflicts in the confirmation dialog and retries with the same idempotency key', async () => {
        const user = userEvent.setup()
        const mockRevalidateStock = vi.fn(() => Promise.resolve(true))
        const mockOnComplete = vi.fn()
        const mockOnError = vi.fn()
        const mockOnClose = vi.fn()

        const items = [
            {
                productId: 'prod-1',
                variantId: null,
                unitId: null,
                name: 'Test Product',
                quantity: 1,
                unitPrice: 100,
                discountAmount: 0,
                taxRate: 0,
            },
        ]

        apiMock.put
            .mockRejectedValueOnce({
                response: {
                    status: 409,
                    data: {
                        message: 'A sale request with the same payment attempt id is already being processed.',
                    },
                },
            })
            .mockResolvedValueOnce({
                data: {
                    invoiceId: 'sale-123',
                    invoiceNumber: 'INV-001',
                },
            })

        render(
            <PaymentModal
                total={100}
                items={items}
                customer={null}
                discountType="fixed"
                discountValue={0}
                warehouseId="warehouse-1"
                paymentAttemptId="edit-attempt-1"
                onRevalidateStock={mockRevalidateStock}
                onClose={mockOnClose}
                onComplete={mockOnComplete}
                onError={mockOnError}
                editingSaleId="sale-123"
                editingMutationVersion={7}
            />,
            { wrapper: TestWrapper }
        )

        await user.click(screen.getByRole('button', { name: /complete sale/i }))
        const confirmButton = await screen.findByRole('button', { name: /confirm payment/i })
        await user.click(confirmButton)

        await waitFor(() => {
            expect(mockOnError).toHaveBeenCalledWith(
                'A sale request with the same payment attempt id is already being processed.'
            )
        })

        expect(screen.getByRole('button', { name: /confirm payment/i })).toBeInTheDocument()
        const firstHeaders = apiMock.put.mock.calls[0][2].headers

        await user.click(screen.getByRole('button', { name: /confirm payment/i }))

        await waitFor(() => {
            expect(mockOnComplete).toHaveBeenCalledWith(
                expect.objectContaining({
                    invoiceId: 'sale-123',
                    attemptId: 'edit-attempt-1',
                })
            )
        })

        const retryHeaders = apiMock.put.mock.calls[1][2].headers
        expect(retryHeaders['Idempotency-Key']).toBe(firstHeaders['Idempotency-Key'])
        expect(retryHeaders['X-Request-Id']).toBe(firstHeaders['X-Request-Id'])
        expect(retryHeaders['X-Payment-Attempt-Id']).toBe('edit-attempt-1')
        expect(apiMock.put.mock.calls[1][1]).toEqual(expect.objectContaining({
            expectedMutationVersion: 7,
        }))
    })
})

describe('PaymentModal sale submission errors', () => {
    const t = (key: string, fallback?: string) => fallback ?? key

    it('warns cashiers that a timed-out payment attempt is ambiguous before retrying', () => {
        const message = getSaleSubmissionErrorMessage({ code: 'ECONNABORTED' }, t)

        expect(message).toBe('Payment request timed out. Check sales history before trying again.')
        expect(message).toContain('Check sales history')
    })

    it('keeps server validation messages for rejected sale submissions', () => {
        const message = getSaleSubmissionErrorMessage({
            response: {
                status: 409,
                data: {
                    message: 'Insufficient stock',
                },
            },
        }, t)

        expect(message).toBe('Insufficient stock')
    })
})

describe('PaymentModal accessibility guard', () => {
    it('links amount validation feedback to the amount input', async () => {
        const user = userEvent.setup()
        const mockRevalidateStock = vi.fn(() => Promise.resolve(true))
        const mockOnComplete = vi.fn()
        const mockOnError = vi.fn()
        const mockOnClose = vi.fn()

        const items = [
            {
                productId: 'prod-1',
                variantId: null,
                unitId: null,
                name: 'Test Product',
                quantity: 1,
                unitPrice: 100,
                discountAmount: 0,
                taxRate: 0,
            },
        ]

        render(
            <PaymentModal
                total={100}
                items={items}
                customer={null}
                discountType="fixed"
                discountValue={0}
                warehouseId="warehouse-1"
                paymentAttemptId="attempt-1"
                onRevalidateStock={mockRevalidateStock}
                onClose={mockOnClose}
                onComplete={mockOnComplete}
                onError={mockOnError}
                editingSaleId={null}
            />,
            { wrapper: TestWrapper }
        )

        // Find the cash amount input by its label
        const cashAmountInput = screen.getByLabelText(/cash amount/i)
        expect(cashAmountInput).toBeInTheDocument()
        expect(cashAmountInput).toHaveAttribute('id', 'payment-cash-amount')

        // Verify the label element has the correct htmlFor attribute
        const labelElement = screen.getByText(/cash amount/i).closest('label')
        expect(labelElement).toHaveAttribute('for', 'payment-cash-amount')

        // Clear the cash amount to trigger validation error
        await user.clear(cashAmountInput)
        
        // Try to submit with invalid amount
        const submitButton = screen.getByRole('button', { name: /complete sale/i })
        await user.click(submitButton)

        // Validation message should appear with proper ARIA role
        const validationMessage = await screen.findByRole('alert')
        expect(validationMessage).toBeInTheDocument()
        expect(validationMessage).toHaveAttribute('id', 'payment-validation-message')

        // Input should have aria-invalid set to true
        expect(cashAmountInput).toHaveAttribute('aria-invalid', 'true')
        
        // Input should have aria-describedby pointing to the validation message
        expect(cashAmountInput).toHaveAttribute('aria-describedby', 'payment-validation-message')
    })
})

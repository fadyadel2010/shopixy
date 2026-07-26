import { describe, expect, it } from 'vitest'
import { requireCurrentSuperAdminPaymentSelection, requireSuperAdminPaymentsPresentation } from './paymentOwnershipAuthority'

const request = { paymentRequestId: '11111111-1111-4111-8111-111111111111', vendorId: '22222222-2222-4222-8222-222222222222', paymentMethodId: '33333333-3333-4333-8333-333333333333', vendorName: 'Vendor', methodName: 'Bank', destinationValue: 'Account', instructions: 'Use reference', amount: 10, currency: 'EGP', status: 'PendingVerification' as const, createdAtUtc: '2030-01-01T00:00:00Z', hasProof: true }

describe('Super Admin payment ownership presentation', () => {
    it('rejects fabricated or malformed monitoring payment rows', () => {
        expect(() => requireSuperAdminPaymentsPresentation({ requests: [{ ...request, vendorId: 'fabricated' }] })).toThrow(/ownership authority rejected/i)
    })
    it('does not preserve a selected monitoring row after current authority removes it', () => {
        expect(requireCurrentSuperAdminPaymentSelection(request.paymentRequestId, [])).toBeNull()
    })
})

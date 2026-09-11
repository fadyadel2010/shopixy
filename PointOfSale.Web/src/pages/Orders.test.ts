import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import {
    getDirectOrderStatusMutationOptions,
    getLifecycleMutationErrorMessage,
    isFulfillmentActionExecutableFromOrdersDrawer,
    isPaymentActionExecutableFromOrdersDrawer,
    normalizeFilters,
    validateManualDeliveryEvidence,
    validateReturnReceiptEvidence,
} from './Orders.jsx'

const currentDir = dirname(fileURLToPath(import.meta.url))
const ordersPath = join(currentDir, 'Orders.jsx')
const adminOrdersApiPath = join(currentDir, '..', 'services', 'adminOrdersApi.js')

describe('Admin Orders lifecycle UI contract', () => {
    it('uses only backend-issued targets for the direct status mutation control', () => {
        const options = getDirectOrderStatusMutationOptions({
            status: 'Confirmed',
            rowVersion: '42',
            availableOrderActions: [
                { code: 'place-on-hold', targetStatus: 'OnHold' },
                { code: 'cancel-order' },
            ],
        })

        expect(options).toEqual(['Confirmed', 'OnHold'])
        expect(options).not.toContain('Shipped')
        expect(options).not.toContain('Delivered')
        expect(options).not.toContain('Completed')
        expect(options).not.toContain('Cancelled')
        expect(options).not.toContain('Refunded')
        expect(options).not.toContain('Processing')
    })

    it('does not reconstruct the OnHold recovery target from order status', () => {
        const options = getDirectOrderStatusMutationOptions({
            status: 'OnHold',
            rowVersion: '42',
            availableOrderActions: [{ code: 'resume-pending', targetStatus: 'Pending' }],
        })

        expect(options).toEqual(['OnHold', 'Pending'])
        expect(getDirectOrderStatusMutationOptions({ status: 'OnHold', availableOrderActions: [] }))
            .toEqual(['OnHold'])
    })

    it('renders backend-issued fulfillment authority separately from generic status mutation', () => {
        const source = readFileSync(ordersPath, 'utf8')
        const service = readFileSync(adminOrdersApiPath, 'utf8')

        expect(source).toContain('getAdminOrderFulfillmentActions(selectedOrderId, signal)')
        expect(source).toContain('requireShipmentActionAuthority(authority, selectedOrderId)')
        expect(source).toContain('runAdminOrderFulfillmentAction(selectedOrderId, actionCode, payload)')
        expect(service).toContain('api.get(`/orders/${orderId}/fulfillment/actions`')
        expect(service).toContain('api.post(')
        expect(service).toContain('`/orders/${orderId}/fulfillment/${actionCode}`')
    })

    it('executes backend-advertised fulfillment actions and gathers evidence for sensitive transitions', () => {
        expect(isFulfillmentActionExecutableFromOrdersDrawer({ code: 'confirm' })).toBe(true)
        expect(isFulfillmentActionExecutableFromOrdersDrawer({ code: 'pack' })).toBe(true)
        expect(isFulfillmentActionExecutableFromOrdersDrawer({ code: 'complete' })).toBe(true)
        expect(isFulfillmentActionExecutableFromOrdersDrawer({ code: 'delivered' })).toBe(true)
        expect(isFulfillmentActionExecutableFromOrdersDrawer({ code: 'returned' })).toBe(true)
        expect(isFulfillmentActionExecutableFromOrdersDrawer({ code: 'failed-delivery' })).toBe(true)

        const source = readFileSync(ordersPath, 'utf8')
        const service = readFileSync(adminOrdersApiPath, 'utf8')
        expect(source).toContain('Delivery reference')
        expect(source).toContain('Proof of delivery URL')
        expect(source).toContain('Attempt number')
        expect(source).toContain('Return authorization (optional)')
        expect(service).toContain("'delivered'")
        expect(service).toContain("'failed-delivery'")
    })

    it('keys each list request by the complete filter state and exposes an explicit retry', () => {
        const source = readFileSync(ordersPath, 'utf8')

        expect(source).toContain("queryKey: [...authorityScope, 'list', filters]")
        expect(source).toContain('onClick={() => ordersQuery.refetch()}')
        expect(source).toContain('Code: ${orderListError.code}')
        expect(source).toContain('Trace: ${orderListError.traceId}')
    })

    it('normalizes malformed URL filter state to the supported server contract', () => {
        const filters = normalizeFilters(new URLSearchParams({
            page: '-5',
            pageSize: '999',
            status: 'Fabricated',
            paymentStatus: 'Unknown',
            sortBy: 'drop table orders',
            sortDirection: 'sideways',
        }))

        expect(filters).toMatchObject({
            page: 1,
            pageSize: 20,
            status: '',
            paymentStatus: '',
            sortBy: 'createdAt',
            sortDirection: 'desc',
        })
    })

    it('keeps admitted cached rows during refresh and only renders empty after a successful read', () => {
        const source = readFileSync(ordersPath, 'utf8')

        expect(source).toContain('if (ordersQuery.data)')
        expect(source).toContain('ordersQuery.isSuccess && !ordersQuery.isFetching && !orders.length')
        expect(source).toContain('disabled={ordersQuery.isFetching}')
        expect(source).toContain('queryFn: ({ signal }) => getAdminOrders(filters, signal)')
    })

    it('keeps an order readable when optional Affiliate evidence is unavailable', () => {
        const source = readFileSync(ordersPath, 'utf8')

        expect(source).toContain('Affiliate attribution is unavailable for this order. Order details remain available.')
        expect(source).toContain('value="Affiliate attribution unavailable"')
        expect(source).not.toContain('Affiliate order lineage authority rejected incomplete attribution evidence.')
    })

    it('exposes every canonical payment status as a server-side filter candidate', () => {
        const source = readFileSync(ordersPath, 'utf8')

        expect(source).toContain("'PartiallyPaid'")
        expect(source).toContain("'PartiallyRefunded'")
        expect(source).toContain("'Expired'")
        expect(source).toContain("'AmountMismatch'")
    })

    it('validates every manual delivery evidence defect class before mutation', () => {
        expect(validateManualDeliveryEvidence({ deliveryReference: '', proofOfDeliveryUrl: '' }))
            .toBe('Delivery reference is required.')
        expect(validateManualDeliveryEvidence({ deliveryReference: 'POD-1', proofOfDeliveryUrl: '' }))
            .toBe('Proof of delivery URL is required.')
        expect(validateManualDeliveryEvidence({ deliveryReference: 'POD-1', proofOfDeliveryUrl: '/relative/pod' }))
            .toBe('Proof of delivery must be an absolute HTTP or HTTPS URL.')
        expect(validateManualDeliveryEvidence({ deliveryReference: 'POD-1', proofOfDeliveryUrl: 'javascript:alert(1)' }))
            .toBe('Proof of delivery must be an absolute HTTP or HTTPS URL.')
        expect(validateManualDeliveryEvidence({ deliveryReference: 'POD-1', proofOfDeliveryUrl: 'https://example.test/a b' }))
            .toBe('Proof of delivery URL cannot contain whitespace.')
        expect(validateManualDeliveryEvidence({ deliveryReference: ' POD-1 ', proofOfDeliveryUrl: ' https://example.test/pod ' }))
            .toBeNull()
    })

    it('executes only backend-advertised payment action codes', () => {
        expect(isPaymentActionExecutableFromOrdersDrawer({ actionCode: 'cod-delivered' })).toBe(true)
        expect(isPaymentActionExecutableFromOrdersDrawer({ actionCode: 'refund' })).toBe(true)
        expect(isPaymentActionExecutableFromOrdersDrawer({ actionCode: 'mark-delivered' })).toBe(false)
        expect(isPaymentActionExecutableFromOrdersDrawer({ actionCode: 'retry-payment' })).toBe(false)
    })

    it('uses safe stale-conflict semantics and refreshes without replay', () => {
        expect(getLifecycleMutationErrorMessage({ status: 409, message: 'raw backend detail' }))
            .toBe('The order changed. Canonical data was refreshed; review it before trying again.')

        const source = readFileSync(ordersPath, 'utf8')
        expect(source).toContain("if (error?.status === 409)")
        expect(source).toContain('void refreshCanonicalState()')
        expect(source).not.toContain('statusMutation.mutate(candidate)\n            void refreshCanonicalState()')
    })

    it('keeps payment, fulfillment, shipment, return, refund and cancellation commands distinct', () => {
        const source = readFileSync(ordersPath, 'utf8')
        expect(source).toContain('Payment lifecycle')
        expect(source).toContain('Shipment evidence')
        expect(source).toContain('cancel-order')
        expect(source).toContain('cancel-shipment')
        expect(source).toContain('A refund changes payment state only')
        expect(source).toContain('shouldRefund: false')
    })

    it('renders item refund and return evidence while keeping vendor authority receipt-only', () => {
        const source = readFileSync(ordersPath, 'utf8')
        const service = readFileSync(adminOrdersApiPath, 'utf8')

        expect(source).toContain('item.refunds?.length')
        expect(source).toContain('Customer refund')
        expect(source).toContain('Physical return')
        expect(source).toContain('returnRequest.canConfirmReceipt')
        expect(source).toContain('This records vendor receipt only. It does not execute the customer refund.')
        expect(service).toContain('`/pos-admin/returns/${returnId}/confirm-receipt`')
        expect(source).not.toContain('returnRequest.canSchedulePickup')
        expect(source).not.toContain('returnRequest.canMarkPickedUp')
        expect(source).not.toContain('returnRequest.canMarkDelivered')
    })

    it('validates vendor receipt quantities against backend-approved return quantity', () => {
        expect(validateReturnReceiptEvidence({ resellableQuantity: '1', damagedQuantity: '0' }, 1)).toBeNull()
        expect(validateReturnReceiptEvidence({ resellableQuantity: '0', damagedQuantity: '0' }, 1))
            .toBe('At least one received item is required.')
        expect(validateReturnReceiptEvidence({ resellableQuantity: '1.5', damagedQuantity: '0' }, 2))
            .toBe('Receipt quantities must be whole numbers.')
        expect(validateReturnReceiptEvidence({ resellableQuantity: '1', damagedQuantity: '1' }, 1))
            .toBe('Receipt quantities cannot exceed the approved return quantity.')
    })
})

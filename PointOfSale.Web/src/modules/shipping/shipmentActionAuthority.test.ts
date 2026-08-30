import { describe, expect, it } from 'vitest'
import { hasShipmentActionAuthority, requireShipmentActionAuthority } from './shipmentActionAuthority'

const authority = {
    orderId: 'order-1',
    orderNumber: 'CH-1001',
    currentStatus: 'Confirmed',
    availableActions: [
        {
            code: 'cancel',
            label: 'Cancel',
            targetStatus: 'Cancelled',
            requiresConfirmation: true,
            requiredFields: [],
        },
    ],
}

describe('Shipment action authority', () => {
    it('accepts backend-issued fulfillment actions for the current order target', () => {
        expect(requireShipmentActionAuthority(authority, 'order-1')).toBe(authority)
        expect(hasShipmentActionAuthority(authority, { orderId: 'order-1' }, 'cancel')).toBe(true)
    })

    it('rejects route or selected-shipment target mismatches', () => {
        expect(() => requireShipmentActionAuthority(authority, 'order-2')).toThrow(/order-target mismatch/)
        expect(hasShipmentActionAuthority(authority, { orderId: 'order-2' }, 'cancel')).toBe(false)
    })

    it('rejects fabricated or malformed action evidence', () => {
        expect(() => requireShipmentActionAuthority({ ...authority, availableActions: [{ ...authority.availableActions[0], code: 'frontend-cancel' }] }, 'order-1')).toThrow(/action evidence/)
        expect(() => requireShipmentActionAuthority({ ...authority, availableActions: [{ ...authority.availableActions[0], requiresConfirmation: 'yes' }] }, 'order-1')).toThrow(/action evidence/)
        expect(() => requireShipmentActionAuthority({ ...authority, availableActions: [{ ...authority.availableActions[0], code: 'delivered', targetStatus: 'Delivered' }] }, 'order-1')).toThrow(/action evidence/)
    })
})

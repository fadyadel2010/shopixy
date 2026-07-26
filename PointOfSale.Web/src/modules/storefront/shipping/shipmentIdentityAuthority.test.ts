import { describe, expect, it } from 'vitest'
import { requireBackendShipmentIdentity, requireBackendTrackedOrder } from './shipmentIdentityAuthority'

const orderId = '11111111-1111-4111-8111-111111111111'
const shipmentId = '22222222-2222-4222-8222-222222222222'
const shipment = { shipmentId, orderId, shipmentNumber: 'SHP-2026-00001', trackingNumber: 'TRK-1' }

describe('Shipment identity authority', () => {
    it('accepts only a canonical backend shipment bound to its order', () => {
        expect(requireBackendShipmentIdentity(shipment, orderId)).toBe(shipment)
        expect(requireBackendTrackedOrder({ id: orderId, shipment })).toEqual({ id: orderId, shipment })
    })

    it('rejects fabricated, conflicting, or cross-order shipment evidence', () => {
        expect(() => requireBackendShipmentIdentity({ trackingNumber: 'forged' }, orderId)).toThrow(/canonical backend identity/)
        expect(() => requireBackendShipmentIdentity({ ...shipment, id: '33333333-3333-4333-8333-333333333333' }, orderId)).toThrow(/conflicting/)
        expect(() => requireBackendShipmentIdentity({ ...shipment, orderId: '33333333-3333-4333-8333-333333333333' }, orderId)).toThrow(/requested order/)
    })

    it('does not allow tracking-only fallback data to become a shipment', () => {
        expect(() => requireBackendTrackedOrder({ id: orderId, shipment: { trackingNumber: 'TRK-1' } })).toThrow(/canonical backend identity/)
    })
})

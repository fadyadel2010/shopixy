// Shipment records are backend-issued evidence only.  The browser may retain a
// route, response, or BFCache snapshot, but it must never turn that data into a
// shipment identity without a complete, internally consistent backend DTO.

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const isGuid = (value) => typeof value === 'string' && guidPattern.test(value)
const nonEmptyText = (value) => typeof value === 'string' && value.trim().length > 0

export function requireBackendShipmentIdentity(candidate, expectedOrderId = null) {
    if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate)) {
        throw new Error('Shipment evidence must be a backend object.')
    }

    const shipmentId = candidate.shipmentId ?? candidate.id
    if (!isGuid(shipmentId) || !isGuid(candidate.orderId) || !nonEmptyText(candidate.shipmentNumber)) {
        throw new Error('Shipment evidence is missing canonical backend identity.')
    }

    if (candidate.shipmentId && candidate.id && candidate.shipmentId !== candidate.id) {
        throw new Error('Shipment evidence has conflicting shipment identities.')
    }

    if (expectedOrderId && candidate.orderId !== expectedOrderId) {
        throw new Error('Shipment evidence does not belong to the requested order.')
    }

    return candidate
}

export function requireBackendTrackedOrder(candidate) {
    if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate) || !isGuid(candidate.id)) {
        throw new Error('Tracked order evidence is missing canonical backend identity.')
    }

    if (candidate.shipment != null) {
        requireBackendShipmentIdentity(candidate.shipment, candidate.id)
    }

    return candidate
}

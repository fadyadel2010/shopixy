const FULFILLMENT_ACTION_CODES = new Set([
    'confirm',
    'pack',
    'out-for-delivery',
    'delivered',
    'complete',
    'failed-delivery',
    'retry-delivery',
    'returned',
    'cancel',
])
const REQUIRED_FIELDS_BY_ACTION = new Map([
    ['delivered', ['deliveryReference', 'proofOfDeliveryUrl']],
    ['failed-delivery', ['reason']],
    ['returned', ['reason']],
])

function isNonEmptyString(value) {
    return typeof value === 'string' && value.trim().length > 0
}

function normalizeId(value) {
    return isNonEmptyString(value) ? value.trim().toLowerCase() : ''
}

export function requireShipmentActionAuthority(authority, expectedOrderId) {
    if (!authority || normalizeId(authority.orderId) !== normalizeId(expectedOrderId)) {
        throw new Error('Shipment action authority rejected order-target mismatch.')
    }

    if (!Array.isArray(authority.availableActions)) {
        throw new Error('Shipment action authority rejected missing backend action evidence.')
    }

    const seenCodes = new Set()
    for (const action of authority.availableActions) {
        const requiredFields = action?.requiredFields
        const expectedFields = REQUIRED_FIELDS_BY_ACTION.get(action?.code) || []
        // Dispatch may be non-shipping (no fields) or bound to an original
        // shipment (trackingNumber). Both are explicit backend contracts.
        const shipmentDispatch = action?.code === 'out-for-delivery' &&
            Array.isArray(requiredFields) && requiredFields.length === 1 && requiredFields[0] === 'trackingNumber'
        if (
            !isNonEmptyString(action?.code) ||
            !FULFILLMENT_ACTION_CODES.has(action.code) ||
            seenCodes.has(action.code) ||
            !isNonEmptyString(action.label) ||
            !isNonEmptyString(action.targetStatus) ||
            typeof action.requiresConfirmation !== 'boolean' ||
            !Array.isArray(requiredFields) ||
            (!shipmentDispatch && requiredFields.length !== expectedFields.length) ||
            (!shipmentDispatch && expectedFields.some((field) => !requiredFields.includes(field))) ||
            new Set(requiredFields).size !== requiredFields.length
        ) {
            throw new Error('Shipment action authority rejected malformed backend action evidence.')
        }

        seenCodes.add(action.code)
    }

    return authority
}

export function hasShipmentActionAuthority(authority, shipment, actionCode) {
    if (!shipment || !authority || normalizeId(authority.orderId) !== normalizeId(shipment.orderId)) {
        return false
    }

    return authority.availableActions.some((action) => action.code === actionCode)
}

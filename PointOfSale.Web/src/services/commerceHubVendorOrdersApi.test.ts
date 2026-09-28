import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import {
    getCommerceHubVendorReturnActions,
    requireCommerceHubVendorActionAuthority,
    requireCommerceHubVendorOrderAuthority,
    requireCommerceHubVendorOrderListAuthority,
    requireCommerceHubVendorReturnAuthority,
} from './commerceHubVendorOrdersApi'

const tenantId = 'a6c9e71e-dd48-49cb-947a-6f460423360e'
const user = { id: '11111111-1111-4111-8111-111111111111', tenantId }
const subOrderId = 'c06062a1-f245-47ef-8490-335f2ac6b9ce'
const parentId = '5bbf6683-a683-429f-a6c7-2ff0395571c1'
const returnId = '2744962e-50e4-45b8-9d03-a0f6d86671c5'

const returnRequest = {
    returnId,
    refundId: '4b3a80dc-9571-4843-90a9-c2096c5b8262',
    orderItemId: '11111111-1111-4111-8111-111111111111',
    customerId: '22222222-2222-4222-8222-222222222222',
    vendorId: tenantId,
    status: 'Pending',
    canSchedulePickup: true,
    canMarkPickedUp: false,
    canMarkInTransit: false,
    canMarkDelivered: false,
    canMarkFailed: true,
    canConfirmReceipt: false,
}

const marketplaceOrder = {
    tenantId,
    orderId: subOrderId,
    orderNumber: 'ORD-CH-0004-A',
    source: 'Marketplace',
    sourceLabel: 'Marketplace',
    sourceOrderId: parentId,
    isMarketplaceOrder: true,
    status: 'Delivered',
    paymentStatus: 'Collected',
    marketplaceLineage: {
        tenantId,
        orderId: subOrderId,
        marketplaceOrderId: parentId,
        marketplaceName: 'CommerceHub',
        marketplaceOrderNumber: 'ORD-CH-0004',
        marketplaceSubOrderNumber: 'ORD-CH-0004-A',
        vendorId: tenantId,
        vendorName: 'Vendor A',
        marketplaceSourceType: 'Marketplace',
        marketplaceStatus: 'Delivered',
        marketplacePaymentStatus: 'Collected',
        currency: 'EGP',
        subtotal: 1320.01,
        shipping: 0,
        total: 1320.01,
    },
}

describe('CommerceHub vendor orders API authority', () => {
    it('accepts only marketplace suborders for the current vendor', () => {
        expect(requireCommerceHubVendorOrderAuthority(marketplaceOrder, user, subOrderId)).toBe(marketplaceOrder)
        expect(requireCommerceHubVendorOrderListAuthority({ items: [marketplaceOrder] }, user).items).toHaveLength(1)
        expect(() => requireCommerceHubVendorOrderListAuthority({ items: [{ ...marketplaceOrder, source: 'Ecommerce', isMarketplaceOrder: false }] }, user)).toThrow()
        expect(() => requireCommerceHubVendorOrderAuthority({ ...marketplaceOrder, tenantId: '83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5' }, user, subOrderId)).toThrow()
    })

    it('binds action authority to tenant, store, suborder, and lifecycle version', () => {
        const authority = {
            tenantId,
            storeId: tenantId,
            subOrderId,
            lifecycleVersion: 7,
            paymentReady: true,
            inventoryReady: true,
            availableActions: [{ code: 'deliver', targetStatus: 'Delivered', requiresReason: false }],
        }
        expect(requireCommerceHubVendorActionAuthority(authority, user, subOrderId)).toBe(authority)
        expect(() => requireCommerceHubVendorActionAuthority({ ...authority, storeId: parentId }, user, subOrderId)).toThrow()
        expect(() => requireCommerceHubVendorActionAuthority({ ...authority, availableActions: [{ code: 'complete', targetStatus: 'Completed', requiresReason: false }] }, user, subOrderId)).toThrow()
    })

    it('accepts one backend-granted vendor return step and rejects cross-tenant or contradictory grants', () => {
        expect(requireCommerceHubVendorReturnAuthority(returnRequest, user, returnId)).toBe(returnRequest)
        expect(getCommerceHubVendorReturnActions(returnRequest).map((action) => action.code)).toEqual(['schedulePickup', 'markFailed'])
        expect(() => requireCommerceHubVendorReturnAuthority({ ...returnRequest, vendorId: parentId }, user, returnId)).toThrow(/cross-scope/i)
        expect(() => requireCommerceHubVendorReturnAuthority({ ...returnRequest, canMarkPickedUp: true }, user, returnId)).toThrow(/contradictory/i)
    })

    it('refetches backend actions and preserves expectedVersion before mutation', () => {
        const source = readFileSync(join(dirname(fileURLToPath(import.meta.url)), 'commerceHubVendorOrdersApi.js'), 'utf8')
        expect(source).toContain('`/commercehub/vendor-orders?${buildOrderQueryParams(filters).toString()}`')
        expect(source).toContain('`/commercehub/vendor-orders/${subOrderId}`')
        expect(source).toContain('await getCommerceHubVendorOrderActions(subOrderId)')
        expect(source).toContain('expectedVersion: authority.lifecycleVersion')
        expect(source).toContain('status: action.targetStatus')
        expect(source).toContain('await getCommerceHubVendorOrder(subOrderId)')
        expect(source).toContain('expectedStatus: acceptedReturn.status')
        expect(source).toContain('`/pos-admin/returns/${returnId}/${action.endpoint}`')
        expect(source).toContain("endpoint: 'mark-failed'")
        expect(source).not.toContain('amount:')
    })
})

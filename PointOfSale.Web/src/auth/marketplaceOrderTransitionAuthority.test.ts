import { describe, expect, it } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'

const repoRoot = path.resolve(__dirname, '../../..')
const read = (...parts: string[]) => fs.readFileSync(path.join(repoRoot, ...parts), 'utf8')

describe('Marketplace order transition authority regression freeze', () => {
    it('re-enters backend order authority before customer or recovery payment execution', () => {
        for (const file of [
            read('PointOfSale.CommerceHub', 'src', 'pages', 'CustomerOrderDetailsPage.tsx'),
            read('PointOfSale.CommerceHub', 'src', 'pages', 'OrderPaymentRecoveryPage.tsx'),
        ]) {
            expect(file).toContain('await orderQuery.refetch()')
            expect(file).toContain('refreshedOrder.isError || !currentOrder?.canRetryPayment')
            expect(file).toContain('orderId: currentOrder.')
        }
    })

    it('does not expose a client-selected generic shipment status transition', () => {
        const hook = read('PointOfSale.Web', 'src', 'modules', 'shipping', 'hooks', 'useShipmentsApi.js')
        expect(hook).toContain('Shipment status transitions require current backend fulfillment action authority.')
        expect(hook).not.toContain('api.put(`/admin/shipments/${shipmentId}/status`')
    })

    it('requires a fresh backend-issued action document for the exact shipment order before cancellation', () => {
        const actions = read('PointOfSale.Web', 'src', 'modules', 'shipping', 'components', 'ShipmentActions.jsx')
        expect(actions).toContain('currentAuthority = await refetchActionAuthority?.()')
        expect(actions).toContain("hasShipmentActionAuthority(currentAuthority, shipment, 'cancel')")
    })
})

import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../i18n/config'
import { OrderDetailsPanel } from './Orders.jsx'
import type { AdminOrderDetails } from '../services/adminOrdersApi'

describe('Manual order drawer dispatch', () => {
    beforeEach(async () => { await i18n.changeLanguage('en') })
    it('survives the order-reset effect and submits only original-shipment-bound tracking', () => {
        const submit = vi.fn()
        const order: AdminOrderDetails = {
            orderId: 'order-1', orderNumber: 'SYN-UI-DISPATCH', tenantId: 'tenant-1', status: 'Processing',
            paymentStatus: 'Pending', fulfillmentStatus: 'Packed', source: 'Ecommerce', rowVersion: '1', items: [], payments: [],
            shipment: { shipmentId: 'original-shipment', status: 'ReadyForPickup' },
        }
        render(<OrderDetailsPanel order={order} onClose={() => {}} onFulfillmentAction={submit}
            fulfillmentAuthority={{ orderId: order.orderId, availableActions: [{ code: 'out-for-delivery', label: 'Dispatch',
                targetStatus: 'OutForDelivery', requiresConfirmation: false, requiredFields: ['trackingNumber'] }] }} />)
        fireEvent.click(screen.getByRole('button', { name: 'Mark out for delivery' }))
        const input = screen.getByRole('textbox', { name: 'Tracking Number' })
        const confirm = screen.getByRole('button', { name: /Confirm.*[Mm]ark out for delivery/ })
        expect(confirm).toBeDisabled()
        fireEvent.change(input, { target: { value: ' SYN-TRACK ' } })
        expect(confirm).toBeEnabled()
        fireEvent.click(confirm)
        expect(submit).toHaveBeenCalledWith({ actionCode: 'out-for-delivery', payload: { shipmentId: 'original-shipment', trackingNumber: 'SYN-TRACK' } })
    })
})

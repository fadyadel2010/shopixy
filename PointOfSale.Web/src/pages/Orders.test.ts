import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import {
    getDirectOrderStatusMutationOptions,
    isFulfillmentActionExecutableFromOrdersDrawer,
} from './Orders.jsx'

const currentDir = dirname(fileURLToPath(import.meta.url))
const ordersPath = join(currentDir, 'Orders.jsx')
const adminOrdersApiPath = join(currentDir, '..', 'services', 'adminOrdersApi.js')

describe('Admin Orders lifecycle UI contract', () => {
    it('does not offer lifecycle-controlled statuses through the generic direct status mutation control', () => {
        const options = getDirectOrderStatusMutationOptions({
            status: 'Confirmed',
            rowVersion: '42',
        })

        expect(options).toEqual(['Confirmed', 'OnHold'])
        expect(options).not.toContain('Shipped')
        expect(options).not.toContain('Delivered')
        expect(options).not.toContain('Completed')
        expect(options).not.toContain('Cancelled')
        expect(options).not.toContain('Refunded')
        expect(options).not.toContain('Processing')
    })

    it('allows OnHold to return only to direct-safe status candidates', () => {
        const options = getDirectOrderStatusMutationOptions({
            status: 'OnHold',
            rowVersion: '42',
        })

        expect(options).toEqual(['Pending', 'OnHold'])
    })

    it('renders backend-issued fulfillment authority separately from generic status mutation', () => {
        const source = readFileSync(ordersPath, 'utf8')
        const service = readFileSync(adminOrdersApiPath, 'utf8')

        expect(source).toContain('getAdminOrderFulfillmentActions(selectedOrderId)')
        expect(source).toContain('requireShipmentActionAuthority(authority, selectedOrderId)')
        expect(source).toContain('runAdminOrderFulfillmentAction(selectedOrderId, actionCode)')
        expect(service).toContain('api.get(`/orders/${orderId}/fulfillment/actions`')
        expect(service).toContain('api.post(')
        expect(service).toContain('`/orders/${orderId}/fulfillment/${actionCode}`')
    })

    it('does not execute fulfillment actions that require extra evidence from the generic drawer buttons', () => {
        expect(isFulfillmentActionExecutableFromOrdersDrawer({ code: 'confirm' })).toBe(true)
        expect(isFulfillmentActionExecutableFromOrdersDrawer({ code: 'pack' })).toBe(true)
        expect(isFulfillmentActionExecutableFromOrdersDrawer({ code: 'complete' })).toBe(true)
        expect(isFulfillmentActionExecutableFromOrdersDrawer({ code: 'delivered' })).toBe(false)
        expect(isFulfillmentActionExecutableFromOrdersDrawer({ code: 'returned' })).toBe(false)
        expect(isFulfillmentActionExecutableFromOrdersDrawer({ code: 'failed-delivery' })).toBe(false)
    })
})

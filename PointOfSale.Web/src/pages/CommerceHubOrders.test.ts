import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const currentDir = dirname(fileURLToPath(import.meta.url))
const page = readFileSync(join(currentDir, 'CommerceHubOrders.jsx'), 'utf8')
const app = readFileSync(join(currentDir, '..', 'App.jsx'), 'utf8')
const sidebar = readFileSync(join(currentDir, '..', 'components', 'Sidebar.jsx'), 'utf8')
const genericOrders = readFileSync(join(currentDir, 'Orders.jsx'), 'utf8')
const english = JSON.parse(readFileSync(join(currentDir, '..', 'i18n', 'locales', 'en.json'), 'utf8'))
const arabic = JSON.parse(readFileSync(join(currentDir, '..', 'i18n', 'locales', 'ar.json'), 'utf8'))

describe('CommerceHub vendor orders surface', () => {
    it('registers dedicated capability-guarded list/detail routes and navigation', () => {
        expect(app).toContain('CommerceHubVendorRouteGuard capability="commercehub.orders.view"')
        expect(app).toContain('path="commercehub/orders"')
        expect(app).toContain('path="commercehub/orders/:subOrderId"')
        expect(sidebar).toContain("path: '/commercehub/orders'")
        expect(sidebar).toContain("capability: 'commercehub.orders.view'")
    })

    it('renders parent/suborder, authoritative readiness, vendor return logistics and receipt gating', () => {
        expect(page).toContain("t('commerceHub.vendorOrders.badge')")
        expect(page).toContain("t('commerceHub.vendorOrders.yourOrder')")
        expect(page).toContain("t('commerceHub.vendorOrders.parentOrder')")
        expect(page).toContain("t('commerceHub.vendorOrders.paymentReadiness')")
        expect(page).toContain("t('commerceHub.vendorOrders.inventoryReadiness')")
        expect(page).toContain('refund.returnRequest?.canConfirmReceipt')
        expect(page).toContain("t('commerceHub.vendorOrders.returnLogistics')")
        expect(page).toContain('getCommerceHubVendorReturnActions(returnRequest)')
        expect(page).toContain('runCommerceHubVendorReturnAction')
        expect(page).toContain('returnActionMutation')
        expect(page).toContain("timestampUtc: timestamp ? new Date(timestamp).toISOString() : null")
        expect(page).toContain('refund.executionState')
        expect(page).toContain('refund.nextAction')
        expect(page).toContain('commerceHubLifecycleHistory')
        expect(page).not.toContain('Request status change')
        expect(english.commerceHub.vendorOrders.badge).toBe('CommerceHub marketplace orders')
        expect(arabic.commerceHub.vendorOrders.badge).toBe('طلبات سوق CommerceHub')
    })

    it('keeps marketplace lifecycle controls out of generic Storefront orders', () => {
        expect(genericOrders).toContain("t('orders.management.details.commerceHubTitle')")
        expect(genericOrders).toContain('navigate(`/commercehub/orders/${order.orderId}`)')
        expect(genericOrders).toContain("!order.isMarketplaceOrder ? <SectionCard title={t('orders.management.details.lifecycle')}>")
    })
})

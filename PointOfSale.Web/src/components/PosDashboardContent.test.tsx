import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const readSource = (path: string) => readFileSync(resolve(process.cwd(), 'src', path), 'utf8')

describe('PosDashboardContent permission gating', () => {
    it('renders admin-only widgets and actions only for permission holders', () => {
        const source = readSource('components/PosDashboardContent.jsx')

        expect(source).toContain("hasPosPermission(user, 'storefront.design.manage')")
        expect(source).toContain('{canViewStorefrontStatus ? <StorefrontStatusCard /> : null}')
        expect(source).toContain("hasPosPermission(user, 'sales.operate')")
        expect(source).toContain("hasPosPermission(user, 'catalog.manage')")
        expect(source).toContain("hasPosPermission(user, 'customers.manage')")
        expect(source).toContain("hasPosPermission(user, 'purchases.manage')")
        expect(source).toContain("hasPosPermission(user, 'inventory.view')")
        expect(source).toContain('{canCreateSale ? (')
        expect(source).toContain('{canManageCatalog ? (')
        expect(source).toContain('{canManageCustomers ? (')
        expect(source).toContain('{canManagePurchases ? (')
        expect(source).toContain('{canViewInventory ? (')
    })

    it('gates the dashboard storefront tab by analytics permission', () => {
        const dashboard = readSource('pages/Dashboard.jsx')

        expect(dashboard).toContain("permission: 'storefront.analytics.view'")
        expect(dashboard).toContain('availableTabs')
    })
})

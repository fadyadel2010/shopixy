import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const readSource = (path: string) => readFileSync(resolve(process.cwd(), 'src', path), 'utf8')

describe('POS permission UI projection', () => {
    it('keeps catalog read screens available without exposing catalog mutations', () => {
        const products = readSource('pages/Products.jsx')
        const categories = readSource('pages/Categories.jsx')
        const units = readSource('pages/Units.jsx')
        const stockModal = readSource('components/ProductStockModal.jsx')

        for (const source of [products, categories, units]) {
            expect(source).toContain("useHasPermission('catalog.manage')")
            expect(source).toContain("useHasPermission('catalog.delete')")
        }

        expect(products).toContain("useHasPermission('catalog.publish')")
        expect(products).toContain("useHasPermission('inventory.adjust')")
        expect(categories).toContain("useHasPermission('catalog.publish')")
        expect(stockModal).toContain('canAdjust && (!showAdjustForm')
    })

    it('keeps permission-backed operational routes discoverable', () => {
        const sidebar = readSource('components/Sidebar.jsx')

        expect(sidebar).toContain("path: '/sales-history'")
        expect(sidebar).toContain("permission: 'sales.view'")
        expect(sidebar).toContain("path: '/treasury/transfers'")
        expect(sidebar).toContain("permission: 'finance.operate'")
    })
})

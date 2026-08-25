import { createRef } from 'react'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import ProductExportPrintArea from './ProductExportPrintArea'

const headers = ['Product', 'SKU', 'Type', 'Category', 'Price', 'Stock', 'Status', 'Publication']

describe('ProductExportPrintArea', () => {
    it('keeps Arabic/mixed text, identifiers, and numbers in explicit bidi contexts', () => {
        render(
            <ProductExportPrintArea
                ref={createRef<HTMLDivElement>()}
                rows={[{
                    id: 'p-1',
                    name: 'قهوة Shopixy طويلة للغاية'.repeat(8),
                    sku: 'SKU-1234567890-VERY-LONG-CODE',
                    typeLabel: 'منتج بسيط',
                    category: 'مشروبات ساخنة'.repeat(5),
                    priceDisplay: '1,234.50 EGP',
                    stockDisplay: 12,
                    stockStateLabel: 'متوفر',
                    statusLabel: 'نشط',
                    publicationLabel: 'منشور',
                }]}
                headers={headers}
                title="تقرير المنتجات"
                storeName="متجر Shopixy"
                scopeLabel="النطاق: العرض الحالي"
                exportedAt="2026-08-24 01:00"
                countLabel="1 منتج"
                filtersSummary="البحث: قهوة"
                isRTL
                dir="rtl"
            />,
        )

        const report = screen.getByText('تقرير المنتجات').closest('.products-export-report')!
        expect(report.parentElement).toHaveAttribute('dir', 'rtl')
        expect(screen.getByText(/قهوة Shopixy/).closest('td')).toHaveAttribute('dir', 'auto')
        expect(screen.getByText('SKU-1234567890-VERY-LONG-CODE').closest('td')).toHaveAttribute('dir', 'ltr')
        expect(screen.getByText('1,234.50 EGP').closest('td')).toHaveClass('number-cell')
        expect(screen.getByText('متوفر')).toHaveClass('stock-state')
        expect(report.querySelectorAll('col')).toHaveLength(8)
        const printCss = report.parentElement?.querySelector('style')?.textContent
        expect(printCss).toContain('overflow-wrap: anywhere')
        expect(printCss).toContain('table-layout: fixed')
    })

    it('chunks a full current view into independently headed print pages', () => {
        const rows = Array.from({ length: 21 }, (_, index) => ({
            id: `p-${index + 1}`,
            name: `Product ${index + 1}`,
            sku: `SKU-${index + 1}`,
            typeLabel: 'Simple',
            category: 'Category',
            priceDisplay: '10.00 EGP',
            stockDisplay: index,
            stockStateLabel: 'In Stock',
            statusLabel: 'Active',
            publicationLabel: 'Published',
        }))

        const { container } = render(
            <ProductExportPrintArea
                rows={rows}
                headers={headers}
                title="Products Report"
                scopeLabel="Current View"
                exportedAt="2026-08-24"
                countLabel="21 products"
                isRTL={false}
                dir="ltr"
            />,
        )

        const pages = container.querySelectorAll('.products-export-page')
        expect(pages).toHaveLength(3)
        expect(Array.from(pages, (page) => page.querySelectorAll('tbody tr').length)).toEqual([7, 7, 7])
        expect(Array.from(pages, (page) => page.querySelectorAll('thead th').length)).toEqual([8, 8, 8])
        expect(container.querySelectorAll('tbody tr')).toHaveLength(21)
    })
})

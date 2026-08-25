// Phase 3 — Product Listing Excel export.
//
// Generates a real .xlsx via the project's existing SheetJS capability
// (same `loadExcelModule()` pattern used by the financial report pages).
// The dataset arrives pre-resolved from productExport.js — this module only
// performs presentation, never filtering or business logic.

import { loadExcelModule } from './reportExports.js'
import {
    EXPORT_SCOPE_SELECTED,
    buildProductExportFilename,
    buildProductExportRows,
} from './productExport.js'

// Readable column widths (in characters), ordered like the header row.
const COLUMN_WIDTHS = [36, 18, 16, 26, 14, 22, 16, 12, 14]
const PRICE_COLUMN_INDEX = 4

export async function exportProductsToExcel(products, { scope, t, formatCurrency, storeName, isRTL = false }) {
    const rows = buildProductExportRows(products, { t, formatCurrency })
    const XLSX = await loadExcelModule()

    const headers = [
        t('products.product'),
        t('products.sku'),
        t('products.type'),
        t('products.category'),
        t('products.price'),
        t('products.stock'),
        t('products.stockState'),
        t('products.status'),
        t('products.publication'),
    ]

    // Prefer real numeric cells for price/stock so Excel stays usable for
    // calculations; fall back to the display string when no number exists.
    const aoa = [headers]
    for (const row of rows) {
        aoa.push([
            row.name,
            row.sku,
            row.typeLabel,
            row.category,
            row.priceValue ?? row.priceDisplay,
            row.stockValue ?? row.stockDisplay,
            row.stockStateLabel,
            row.statusLabel,
            row.publicationLabel,
        ])
    }

    const worksheet = XLSX.utils.aoa_to_sheet(aoa)
    worksheet['!cols'] = COLUMN_WIDTHS.map((wch) => ({ wch }))
    // Frozen header row (ignored silently by writers that do not support it).
    worksheet['!freeze'] = { xSplit: 0, ySplit: 1, topLeftCell: 'A2', activePane: 'bottomLeft', state: 'frozen' }

    // Minimal numeric formatting for the price column where a number exists.
    rows.forEach((row, index) => {
        if (row.priceValue === null) return
        const cell = worksheet[XLSX.utils.encode_cell({ r: index + 1, c: PRICE_COLUMN_INDEX })]
        if (cell) cell.z = '0.00'
    })

    // XLSX cells have an explicit type, unlike CSV. Force every string cell
    // to the OpenXML string type and remove formula metadata so user-controlled
    // values beginning with =, +, - or @ can never become executable formulas.
    for (let rowIndex = 0; rowIndex < aoa.length; rowIndex += 1) {
        for (let columnIndex = 0; columnIndex < aoa[rowIndex].length; columnIndex += 1) {
            const cell = worksheet[XLSX.utils.encode_cell({ r: rowIndex, c: columnIndex })]
            if (!cell || typeof cell.v !== 'string') continue
            cell.t = 's'
            delete cell.f
            delete cell.F
        }
    }

    const workbook = XLSX.utils.book_new()
    // Report context lives in workbook properties so the data sheet stays a
    // clean table. No extra API calls are made to populate this metadata.
    workbook.Props = {
        Title: t('products.exportReportTitle'),
        Subject: `${t('products.exportScope')}: ${scope === EXPORT_SCOPE_SELECTED ? t('products.exportSelected') : t('products.exportCurrentView')}`,
        Author: storeName || 'Shopixy',
        CreatedDate: new Date(),
    }
    workbook.Workbook = {
        ...(workbook.Workbook || {}),
        Views: [{ RTL: !!isRTL }],
    }

    // Sheet names are limited to 31 characters and must not contain []:*?/\
    const sheetName = String(t('products.title')).replace(/[[\]:*?/\\]/g, ' ').slice(0, 31) || 'Products'
    XLSX.utils.book_append_sheet(workbook, worksheet, sheetName)
    // SheetJS requires cellStyles for row/column presentation metadata such as
    // the explicit readable column widths to be persisted to the XLSX file.
    XLSX.writeFile(
        workbook,
        buildProductExportFilename({ format: 'xlsx', scope }),
        { cellStyles: true },
    )
}

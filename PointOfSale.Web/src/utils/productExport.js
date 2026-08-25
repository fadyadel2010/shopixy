// Phase 3 — Product Listing export data source.
//
// The export NEVER re-filters, re-sorts or re-fetches products. It consumes
// the exact derived array the table renders (`sortedProducts`) and, for the
// "Selected" scope, intersects it with the Phase 2 selection state. This
// guarantees the export can never disagree with what the user is looking at.

export const EXPORT_SCOPE_CURRENT_VIEW = 'current-view'
export const EXPORT_SCOPE_SELECTED = 'selected'

// Publication label keys mirror the table badge mapping in Products.jsx
// (Draft=0, Published=1, Archived=2). The status arrives pre-normalized on
// each enriched row via normalizePublishStatus.
const PUBLICATION_LABEL_KEYS = {
    0: 'products.draft',
    1: 'products.published',
    2: 'products.archived',
}

const STOCK_STATE_LABEL_KEYS = {
    inStock: 'products.inStock',
    lowStock: 'products.lowStock',
    outOfStock: 'products.outOfStock',
}

const FALLBACK_CELL = '—'

/**
 * Resolves the export dataset for a scope.
 *
 * - Current View: exactly the visible rows (`sortedProducts`), preserving
 *   the active client-side filter + sort order.
 * - Selected: only rows that are BOTH currently visible AND currently
 *   selected. Walking the visible rows (instead of the selected IDs array)
 *   guarantees stale/hidden IDs from another page can never leak in, and the
 *   export preserves the current view order.
 */
export function getProductsForExport({ scope, sortedProducts, selectedProductIds }) {
    const visibleRows = Array.isArray(sortedProducts) ? sortedProducts : []

    if (scope === EXPORT_SCOPE_SELECTED) {
        const selectedIds = new Set(Array.isArray(selectedProductIds) ? selectedProductIds : [])
        return visibleRows.filter((product) => selectedIds.has(product.id))
    }

    return visibleRows
}

/**
 * Maps visible product rows to export-ready cells. Consumes the same derived
 * fields the table renders (totalStock, stockState, normalizedPublishStatus)
 * so the export can never contradict the listing.
 *
 * Numeric values are kept alongside display strings so Excel can export real
 * numbers while the PDF/print path uses the display strings.
 */
export function buildProductExportRows(products, { t, formatCurrency }) {
    return (Array.isArray(products) ? products : []).map((product) => {
        const tracked = !!product.trackStock
        const variantManaged = tracked && !!product.hasVariants

        // Treat malformed/non-numeric transport values as missing. This keeps
        // Excel from receiving NaN/Infinity and prevents PDF from presenting a
        // missing value as zero through a permissive currency formatter.
        const rawStockValue = product.totalStock
        const numericStockValue = rawStockValue === null || rawStockValue === undefined
            ? NaN
            : Number(rawStockValue)
        const hasNumericStock = Number.isFinite(numericStockValue)

        // Stock presentation mirrors renderStockCell in Products.jsx:
        //  - untracked            -> "—"
        //  - variant-managed      -> "Managed by variants"
        //  - tracked simple       -> numeric quantity
        const stockDisplay = !tracked
            ? FALLBACK_CELL
            : variantManaged
                ? t('products.managedByVariants')
                : hasNumericStock ? numericStockValue : FALLBACK_CELL

        // Stock state mirrors the table badges: variant-managed rows show no
        // badge (redundant next to "Managed by variants"), untracked shows "—".
        const stockStateLabel = !tracked
            ? FALLBACK_CELL
            : variantManaged
                ? ''
                : (STOCK_STATE_LABEL_KEYS[product.stockState] ? t(STOCK_STATE_LABEL_KEYS[product.stockState]) : '')

        // Number(null) === 0 — a missing price must stay empty, never become 0.
        const priceValue = product.salePrice === null || product.salePrice === undefined
            ? NaN
            : Number(product.salePrice)
        const hasNumericPrice = Number.isFinite(priceValue)

        return {
            id: product.id,
            name: product.name || FALLBACK_CELL,
            sku: product.sku || FALLBACK_CELL,
            typeLabel: t(product.hasVariants ? 'products.variantProductType' : 'products.simpleProductType'),
            category: product.categoryName || product.category?.name || FALLBACK_CELL,
            priceValue: hasNumericPrice ? priceValue : null,
            priceDisplay: hasNumericPrice ? (formatCurrency(priceValue) || FALLBACK_CELL) : FALLBACK_CELL,
            stockValue: tracked && !variantManaged && hasNumericStock ? numericStockValue : null,
            stockDisplay,
            stockStateLabel,
            statusLabel: product.isActive ? t('products.active') : t('products.inactive'),
            publicationLabel: t(PUBLICATION_LABEL_KEYS[product.normalizedPublishStatus] ?? 'products.draft'),
        }
    })
}

/**
 * Deterministic, tenant-safe export filenames, e.g.
 * `shopixy-products-current-view.xlsx` / `shopixy-products-selected.pdf`.
 */
export function buildProductExportFilename({ format, scope }) {
    const scopePart = scope === EXPORT_SCOPE_SELECTED ? 'selected' : 'current-view'
    return `shopixy-products-${scopePart}.${format}`
}

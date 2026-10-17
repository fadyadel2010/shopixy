import type { ProductExportRow } from '../components/ProductExportPrintArea';
export const EXPORT_SCOPE_CURRENT_VIEW: 'current-view';
export const EXPORT_SCOPE_SELECTED: 'selected';
export interface ExportProduct {
    id: string; name?: string; sku?: string; categoryName?: string; category?: { name?: string } | null;
    trackStock?: boolean; hasVariants?: boolean; totalStock?: number | string | null; stockState?: string;
    salePrice?: number | string | null; isActive?: boolean; normalizedPublishStatus?: number;
}
export interface ProductExportFormat { t(key: string): string; formatCurrency(value: number): string }
export function getProductsForExport<T extends { id: string }>(request: { scope: string; sortedProducts: T[] | null; selectedProductIds?: string[] | null }): T[];
export function buildProductExportRows(products: ExportProduct[], format: ProductExportFormat): (ProductExportRow & { priceValue: number | null; stockValue: number | null })[];
export function buildProductExportFilename(request: { format: string; scope: string }): string;

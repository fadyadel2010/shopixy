import type { ExportProduct, ProductExportFormat } from './productExport';
export function exportProductsToExcel(products: ExportProduct[], options: ProductExportFormat & { scope: string; storeName?: string; isRTL?: boolean }): Promise<void>;

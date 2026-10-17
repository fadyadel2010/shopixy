import type { ForwardRefExoticComponent, RefAttributes } from 'react';
export interface ProductExportRow {
    id?: string; name: string; sku: string; typeLabel: string; category: string; priceDisplay: string;
    stockDisplay: string | number; stockStateLabel?: string; statusLabel: string; publicationLabel: string;
}
declare const ProductExportPrintArea: ForwardRefExoticComponent<{
    rows: ProductExportRow[]; headers: string[]; title: string; storeName?: string; scopeLabel: string;
    exportedAt: string; countLabel: string; filtersSummary?: string; isRTL: boolean; dir: 'ltr' | 'rtl';
} & RefAttributes<HTMLDivElement>>;
export default ProductExportPrintArea;

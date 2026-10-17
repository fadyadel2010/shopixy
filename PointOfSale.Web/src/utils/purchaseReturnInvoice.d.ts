export interface PurchaseReturnSourceItem {
    originalInvoiceItemIds: string[]; productId: string; productVariantId?: string | null; productName: string; variantSku?: string | null;
    variantAttributes?: { name?: string; value?: string }[] | null; unitId?: string | null; unitName?: string | null;
    purchasedQuantity: number; returnedQuantity: number; remainingQuantity: number; unitPrice: number; taxRate: number;
}
export function mapPurchaseReturnInvoiceItem(item: PurchaseReturnSourceItem, fallbackUnitName: string): {
    originalInvoiceItemIds: string[]; productId: string; productVariantId?: string | null; name: string; variantDisplayName: string;
    variantSku: string | null; variantAttributes?: PurchaseReturnSourceItem['variantAttributes']; unitId?: string | null; unitName: string;
    purchasedQuantity: number; returnedQuantity: number; originalQuantity: number; quantity: number; unitPrice: number; taxRate: number; reason: string;
};

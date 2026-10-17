export interface ProductAuthoringVariant {
    variantId: string | null; sku: string | null; barcode: string | null; price: number; onlinePrice: number | null;
    costPrice: number; weight: number | null; mediaId: string | null; imageUrl: string | null;
    isActive: boolean; isDefault: boolean; attributes: Record<string, string>; sortOrder?: number;
}
export function toProductAuthoringVariant(variant: unknown, sortOrder?: number): ProductAuthoringVariant;
export function normalizeProductAuthoringVariants(variants: unknown): ProductAuthoringVariant[];

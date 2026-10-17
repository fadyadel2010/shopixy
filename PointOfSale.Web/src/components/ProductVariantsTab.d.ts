import type { ForwardRefExoticComponent, RefAttributes } from 'react';
import type { ProductAuthoringVariant } from '../utils/productAuthoringVariants';
export interface VariantDraftSnapshot { authoringVariants: ProductAuthoringVariant[]; hasPendingChanges: boolean }
export interface VariantAuthoringHandle {
    replaceGeneratedSkus(replacements: Record<string, string>): void; hasPendingChanges(): boolean; getLastError(): string;
    prepareProductAuthoringDraft(): Promise<VariantDraftSnapshot | false>;
}
declare const ProductVariantsTab: ForwardRefExoticComponent<{
    productId?: string | null; productMediaRevision?: number; forceMatrixMode?: boolean; productSkuPrefix?: string;
    onVariantDraftChange?: ((draft: VariantDraftSnapshot) => void) | null;
    onSaveVariantDraft?: ((draft: VariantDraftSnapshot) => Promise<{ id?: string } | false>) | null;
} & RefAttributes<VariantAuthoringHandle>>;
export default ProductVariantsTab;

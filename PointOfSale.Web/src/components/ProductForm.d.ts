import type { ForwardRefExoticComponent, RefAttributes } from 'react';
import type { GeneratedProduct } from './AiProductGeneratorModal';
export interface ProductFormMeta {
    name: string; isActive: boolean; publishStatus: 0 | 1 | 2; isOnlineVisible: boolean;
    slug: string; hasVariants: boolean; loading: boolean;
}
export interface ProductFormHandle {
    hasPendingChanges(): boolean; confirmDiscardPendingChanges(): Promise<boolean>;
    applyAiGeneratedData(product: GeneratedProduct): { appliedFieldCount: number; skippedFieldCount: number };
}
declare const ProductForm: ForwardRefExoticComponent<{
    productId?: string | null; onClose?(): void; onSave?(product: { id?: string; hasVariants: boolean }): void;
    mode?: 'modal' | 'workspace'; formId?: string; hideActions?: boolean; onMetaChange?(meta: ProductFormMeta): void; focusSection?: string;
} & RefAttributes<ProductFormHandle>>;
export default ProductForm;

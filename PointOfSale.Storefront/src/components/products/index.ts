/**
 * Product Components Index
 * 
 * Exports all product card variants and related components.
 * Phase 10.3.6: Added WishlistButton
 * Phase 11.3: Added VariantSelector
 */

export { ProductCardDefault } from './ProductCardDefault';
export { ProductCardCompact } from './ProductCardCompact';
export { ProductCardDetailed } from './ProductCardDetailed';
export { ProductCardFashion } from './ProductCardFashion';
export { ProductCardElectronics } from './ProductCardElectronics';
export {
    useProductCardRuntime,
    ProductCardLink,
    ProductCardMedia,
    ProductCardPrice,
    ProductCardQuickAddButton,
    ProductCardWishlist,
    ProductCardInteractionLayer,
    type ProductCardRuntime,
} from './ProductCardRuntime';
export {
    PRODUCT_CARD_RENDERERS,
    ModernProductCardRenderer,
    MinimalProductCardRenderer,
    FashionProductCardRenderer,
    PearlProductCardRenderer,
    ElectronicsProductCardRenderer,
    type ProductCardRenderer,
} from './ProductCardRenderers';
export {
    resolveProductCardRenderer,
    type ProductCardRendererId,
} from './productCardRendererResolver';
export { default as ProductBadges } from './ProductBadges';
export { WishlistButton } from './WishlistButton';
export { VariantSelector } from './VariantSelector';
export { CategoryPromotionBanner } from './CategoryPromotionBanner';
export { ProductMediaGallery } from './ProductMediaGallery';
export { ProductMediaViewer } from './ProductMediaViewer';
export { ProductGalleryModal } from './ProductGalleryModal';

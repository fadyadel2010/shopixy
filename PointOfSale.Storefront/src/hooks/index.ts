/**
 * Hooks Module Index
 * 
 * Exports all custom React hooks.
 */

export {
    useCategories,
    clearCategoryCache,
    invalidateCategoryCache,
    refreshCategoryCache,
    type UseCategoriesState,
} from './useCategories';
export {
    useHomepageSectionProducts,
    clearHomepageSectionProductsCache,
    invalidateHomepageSectionProductsCache,
    refreshHomepageSectionProductsCache,
    type HomepageSectionProductsRefreshOptions,
    type UseHomepageSectionProductsOptions,
    type UseHomepageSectionProductsState,
} from './useHomepageSectionProducts';
export { useProducts, type UseProductsState, type ProductQueryParams } from './useProducts';
export {
    useProductSearch,
    MIN_SEARCH_LENGTH,
    DEBOUNCE_DELAY_MS,
    type UseProductSearchState,
    type SearchQueryParams,
    type SearchStatus,
    type SearchResult,
} from './useProductSearch';
export {
    useScrollPosition,
    useBodyScrollLock,
    useFocusTrap,
    type ScrollPosition,
} from './useScrollPosition';
export {
    useOrder,
    STATUS_CONFIG,
    STATUS_PROGRESSION,
    type UseOrderResult,
} from './useOrder';
export { useCurrency, type UseCurrencyResult } from './useCurrency';
export {
    useVariantSelector,
    type AttributeGroup,
    type SelectedAttributes,
    type VariantSelectorResult,
} from './useVariantSelector';
export { usePromotionCountdown } from './usePromotionCountdown';
export {
    useProductDiscovery,
    useActiveFilters,
    type DiscoveryQueryParams,
    type UseProductDiscoveryState,
} from './useProductDiscovery';
export {
    useDiscoveryFilters,
    type DiscoveryFilters,
    type DiscoveryFilterActions,
    type UseDiscoveryFiltersResult,
    type UseDiscoveryFiltersOptions,
} from './useDiscoveryFilters';
export {
    useProductListDiscovery,
    type UseProductListDiscoveryResult,
    type UseProductListDiscoveryOptions,
} from './useProductListDiscovery';


/**
 * useProducts Hook
 * 
 * PHASE: Product Discovery Migration
 * 
 * NOW USES ELASTICSEARCH via fetchProductsDiscover endpoint.
 * Maintains backward-compatible interface for existing pages.
 * 
 * ARCHITECTURE:
 * - Elasticsearch is the ONLY data source
 * - Frontend performs NO filtering or sorting
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { fetchProductsDiscover } from '@/services/api';
import type { ProductListItemDto, ProductDiscoveryResult } from '@/types';

/**
 * Product query parameters
 */
export interface ProductQueryParams {
    page: number;
    pageSize: number;
    categoryId?: string;
}

/**
 * Product fetch state
 */
export interface UseProductsState {
    products: ProductListItemDto[];
    pagination: {
        page: number;
        pageSize: number;
        totalCount: number;
        totalPages: number;
        hasNextPage: boolean;
        hasPreviousPage: boolean;
    } | null;
    isLoading: boolean;
    error: string | null;
    refetch: () => Promise<void>;
}

/**
 * Default query params
 */
const DEFAULT_PARAMS: ProductQueryParams = {
    page: 1,
    pageSize: 20,
};

/**
 * Maps ProductDiscoveryItemDto to ProductListItemDto for backward compatibility.
 * This ensures existing components continue to work.
 * 
 * CRITICAL: Preserves hasVariants and displayPriceText for variant-aware pricing.
 */
function mapDiscoveryItemToListItem(item: ProductDiscoveryResult['items'][0]): ProductListItemDto {
    // CRITICAL: Variant products should NOT show "Out of Stock" at the product level
    // because stock is tracked at the variant level. The user must select a variant
    // to see actual availability. So for hasVariants=true, we treat as available.
    const isProductAvailable = item.hasVariants ? true : item.inStock;

    return {
        id: item.id,
        name: item.name,
        nameAr: item.nameAr,
        // CRITICAL: Variant-aware pricing
        // For variant products, price will be null from ES, but we need a number for the type
        // The displayPriceText should be used instead when hasVariants is true
        price: item.price ?? 0,
        effectivePrice: item.price ?? 0,
        pricing: item.pricing,
        categoryName: item.categoryName,
        slug: item.slug,
        storefrontLayout: item.storefrontLayout,
        productTemplate: item.storefrontLayout,
        isFeatured: item.isFeatured,
        thumbnailImage: item.imageUrl,
        // Use computed availability for variant products
        isAvailable: isProductAvailable,
        inStock: isProductAvailable,
        stockQuantity: 0, // Not returned in discovery, but not used in cards
        // PHASE: Variant-Aware Pricing - Pass through for ProductCard
        hasVariants: item.hasVariants,
        displayPriceText: item.displayPriceText,
        // PHASE: Promotions Engine
        hasActivePromotion: item.hasActivePromotion,
        promotionBadge: item.promotionBadge,
        promotionDiscountType: item.promotionDiscountType,
        promotionDiscountValue: item.promotionDiscountValue,
    };
}

/**
 * Hook for fetching products with pagination and filtering.
 * 
 * NOW USES ELASTICSEARCH-POWERED DISCOVERY.
 */
export function useProducts(params: Partial<ProductQueryParams> = {}): UseProductsState {
    const queryParams = { ...DEFAULT_PARAMS, ...params };

    const [products, setProducts] = useState<ProductListItemDto[]>([]);
    const [pagination, setPagination] = useState<UseProductsState['pagination']>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const mountedRef = useRef(true);

    // Track current request to prevent race conditions
    const requestIdRef = useRef(0);

    const loadProducts = useCallback(async () => {
        const currentRequestId = ++requestIdRef.current;

        setIsLoading(true);
        setError(null);

        try {
            // USE ELASTICSEARCH DISCOVERY ENDPOINT
            const result: ProductDiscoveryResult = await fetchProductsDiscover({
                page: queryParams.page,
                pageSize: queryParams.pageSize,
                category: queryParams.categoryId ? [queryParams.categoryId] : undefined,
            });

            // Only update state if this is still the latest request
            if (mountedRef.current && currentRequestId === requestIdRef.current) {
                // Map discovery items to list items for backward compatibility
                setProducts(result.items.map(mapDiscoveryItemToListItem));
                setPagination({
                    page: result.page,
                    pageSize: result.pageSize,
                    totalCount: result.totalCount,
                    totalPages: result.totalPages,
                    hasNextPage: result.hasNextPage,
                    hasPreviousPage: result.page > 1,
                });
                setError(null);
            }
        } catch (err) {
            console.error('[useProducts] Failed to fetch products:', err);
            if (mountedRef.current && currentRequestId === requestIdRef.current) {
                setError('Failed to load products');
                setProducts([]);
                setPagination(null);
            }
        } finally {
            if (mountedRef.current && currentRequestId === requestIdRef.current) {
                setIsLoading(false);
            }
        }
    }, [queryParams.page, queryParams.pageSize, queryParams.categoryId]);

    useEffect(() => {
        mountedRef.current = true;
        loadProducts();

        return () => {
            mountedRef.current = false;
        };
    }, [loadProducts]);

    const refetch = useCallback(async () => {
        await loadProducts();
    }, [loadProducts]);

    return {
        products,
        pagination,
        isLoading,
        error,
        refetch,
    };
}

import { useCallback, useDeferredValue, useEffect, useMemo, useRef, useState } from 'react';
import { getTenantId } from '@/bootstrap';
import {
    resolveHomepageCollectionSource,
    selectHomepageCollectionItems,
    type HomepageCollectionSource,
} from '@/components/sections/homepageCollectionUtils';
import {
    fetchHomepageProductsByIds,
    fetchProductsDiscover,
} from '@/services/api';
import type { ProductDiscoveryResult, ProductListItemDto } from '@/types';

const GUID_PATTERN =
    /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

const productCache = new Map<string, ProductListItemDto[]>();
const inFlightProductRequests = new Map<string, Promise<ProductListItemDto[]>>();

export interface UseHomepageSectionProductsOptions {
    productIds?: string[] | null;
    pageSize?: number;
    source?: HomepageCollectionSource | string | null;
    seedKey?: string;
}

export interface UseHomepageSectionProductsState {
    products: ProductListItemDto[];
    isLoading: boolean;
    error: string | null;
    refetch: () => Promise<void>;
}

function mapDiscoveryItemToListItem(item: ProductDiscoveryResult['items'][0]): ProductListItemDto {
    const availability = item.availability;

    return {
        id: item.id,
        name: item.name,
        nameAr: item.nameAr,
        price: item.price ?? 0,
        effectivePrice: item.price ?? 0,
        pricing: item.pricing,
        categoryName: item.categoryName,
        slug: item.slug,
        storefrontLayout: item.storefrontLayout,
        productTemplate: item.storefrontLayout,
        isFeatured: item.isFeatured,
        thumbnailImage: item.imageUrl,
        availability,
        isAvailable: availability?.isAvailable ?? false,
        inStock: item.inStock,
        stockQuantity: availability?.maximumQuantity ?? 0,
        hasVariants: item.hasVariants,
        displayPriceText: item.displayPriceText,
        hasActivePromotion: item.hasActivePromotion,
        promotionBadge: item.promotionBadge,
        promotionDiscountType: item.promotionDiscountType,
        promotionDiscountValue: item.promotionDiscountValue,
    };
}

function normalizeProductIds(productIds?: string[] | null): string[] | undefined {
    if (!Array.isArray(productIds)) {
        return undefined;
    }

    return Array.from(
        new Set(
            productIds
                .map((productId) => String(productId ?? '').trim())
                .filter(Boolean)
        )
    );
}

function getValidProductIds(productIds: string[]): string[] {
    return productIds.filter((productId) => GUID_PATTERN.test(productId));
}

async function loadProductsForKey(
    cacheKey: string,
    source: HomepageCollectionSource,
    productIds: string[] | undefined,
    pageSize: number,
    seedKey: string
): Promise<ProductListItemDto[]> {
    if (productCache.has(cacheKey)) {
        return productCache.get(cacheKey)!;
    }

    if (inFlightProductRequests.has(cacheKey)) {
        return inFlightProductRequests.get(cacheKey)!;
    }

    const request = (async () => {
        if (source === 'curated') {
            if (!productIds || productIds.length === 0) {
                return [];
            }

            return fetchHomepageProductsByIds(productIds);
        }

        const result = await fetchProductsDiscover({
            page: 1,
            pageSize: source === 'random' ? Math.max(pageSize * 3, 24) : pageSize,
        });

        const discoveredProducts = result.items.map(mapDiscoveryItemToListItem);
        if (source === 'random') {
            return selectHomepageCollectionItems(discoveredProducts, {
                limit: pageSize,
                source,
                seedKey,
                getKey: (product) => product.id,
            });
        }

        return discoveredProducts.slice(0, pageSize);
    })();

    inFlightProductRequests.set(cacheKey, request);

    try {
        const result = await request;
        productCache.set(cacheKey, result);
        return result;
    } finally {
        inFlightProductRequests.delete(cacheKey);
    }
}

export function useHomepageSectionProducts({
    productIds,
    pageSize = 8,
    source,
    seedKey = 'homepage-products',
}: UseHomepageSectionProductsOptions = {}): UseHomepageSectionProductsState {
    const mountedRef = useRef(true);
    const tenantCacheKey = (() => {
        try {
            return getTenantId();
        } catch {
            return 'unresolved';
        }
    })();
    const normalizedSource = useMemo(
        () => resolveHomepageCollectionSource(source, productIds),
        [productIds, source]
    );
    const normalizedProductIds = useMemo(() => normalizeProductIds(productIds), [productIds]);
    const validProductIds = useMemo(
        () => normalizedProductIds ? getValidProductIds(normalizedProductIds) : undefined,
        [normalizedProductIds]
    );

    const cacheKey = useMemo(() => {
        if (normalizedSource !== 'curated') {
            return `${tenantCacheKey}:homepage:${normalizedSource}:${pageSize}:${seedKey}`;
        }

        if (validProductIds && validProductIds.length > 0) {
            return `${tenantCacheKey}:homepage:${validProductIds.join(',')}`;
        }

        if (normalizedProductIds?.length === 0) {
            return `${tenantCacheKey}:homepage:curated-empty`;
        }

        return `${tenantCacheKey}:homepage:curated-invalid`;
    }, [normalizedProductIds, normalizedSource, pageSize, seedKey, tenantCacheKey, validProductIds]);

    const deferredCacheKey = useDeferredValue(cacheKey);
    const [products, setProducts] = useState<ProductListItemDto[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    const loadProducts = useCallback(async (forceRefresh = false) => {
        if (
            deferredCacheKey.endsWith(':homepage:curated-invalid') ||
            deferredCacheKey.endsWith(':homepage:curated-empty')
        ) {
            if (mountedRef.current) {
                setProducts([]);
                setError(null);
                setIsLoading(false);
            }
            return;
        }

        if (forceRefresh) {
            productCache.delete(deferredCacheKey);
        }

        setIsLoading(true);
        setError(null);

        try {
            const result = await loadProductsForKey(
                deferredCacheKey,
                normalizedSource,
                validProductIds,
                pageSize,
                seedKey
            );

            if (mountedRef.current) {
                setProducts(result);
                setError(null);
            }
        } catch (err) {
            console.error('[useHomepageSectionProducts] Failed to fetch homepage products:', err);
            if (mountedRef.current) {
                setProducts([]);
                setError('Failed to load homepage products');
            }
        } finally {
            if (mountedRef.current) {
                setIsLoading(false);
            }
        }
    }, [deferredCacheKey, normalizedSource, pageSize, seedKey, validProductIds]);

    useEffect(() => {
        mountedRef.current = true;
        void loadProducts();

        return () => {
            mountedRef.current = false;
        };
    }, [loadProducts]);

    const refetch = useCallback(async () => {
        await loadProducts(true);
    }, [loadProducts]);

    return {
        products,
        isLoading,
        error,
        refetch,
    };
}

export function clearHomepageSectionProductsCache(): void {
    productCache.clear();
    inFlightProductRequests.clear();
}

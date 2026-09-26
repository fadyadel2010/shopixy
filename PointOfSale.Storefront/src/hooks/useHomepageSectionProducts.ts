import { useCallback, useDeferredValue, useEffect, useMemo, useRef, useState } from 'react';
import {
    resolveHomepageCollectionSource,
    selectHomepageCollectionItems,
    type HomepageCollectionSource,
} from '@/components/sections/homepageCollectionUtils';
import {
    fetchHomepageProductsByIds,
    fetchProductsDiscover,
} from '@/services/api/storefrontApi';
import { useLanguageStore } from '@/i18n/languageStore';
import {
    getRuntimeCacheIdentity,
    isRuntimeCacheFresh,
    type RuntimeCacheLoadOptions,
} from '@/storefront/runtime/cache/runtimeCachePolicy';
import type { ProductDiscoveryResult, ProductListItemDto } from '@/types';
import {
    getHomepageProductIdsIdentity,
    normalizeHomepageProductIds,
} from './homepageProductIdentity';

const GUID_PATTERN =
    /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;

interface HomepageProductCacheEntry {
    generation: number;
    inFlight: Promise<ProductListItemDto[]> | null;
    loadedAt: number | null;
    products: ProductListItemDto[] | null;
}

const productCache = new Map<string, HomepageProductCacheEntry>();

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

export interface HomepageSectionProductsRefreshOptions
    extends UseHomepageSectionProductsOptions, RuntimeCacheLoadOptions {}

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

function getValidProductIds(productIds: string[]): string[] {
    return productIds.filter((productId) => GUID_PATTERN.test(productId));
}

async function loadProductsForKey(
    cacheKey: string,
    source: HomepageCollectionSource,
    productIds: string[] | undefined,
    pageSize: number,
    seedKey: string,
    options: RuntimeCacheLoadOptions = {}
): Promise<ProductListItemDto[]> {
    let entry = productCache.get(cacheKey);
    if (!entry) {
        entry = {
            generation: 0,
            inFlight: null,
            loadedAt: null,
            products: null,
        };
        productCache.set(cacheKey, entry);
    }

    if (entry.inFlight) {
        return entry.inFlight;
    }

    if (!options.force
        && entry.products
        && isRuntimeCacheFresh(entry.loadedAt, cacheKey, cacheKey)) {
        return entry.products;
    }

    if (options.force) {
        entry.generation += 1;
        entry.loadedAt = null;
    }

    const requestGeneration = entry.generation;
    const request = (async () => {
        if (source === 'curated') {
            if (!productIds || productIds.length === 0) {
                return [];
            }

            return (await fetchHomepageProductsByIds(productIds)).slice(0, pageSize);
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

    entry.inFlight = request;

    try {
        const result = await request;

        if (productCache.get(cacheKey) === entry && entry.generation === requestGeneration) {
            entry.products = result;
            entry.loadedAt = Date.now();
        }

        return result;
    } finally {
        if (productCache.get(cacheKey) === entry && entry.inFlight === request) {
            entry.inFlight = null;
        }
    }
}

function createHomepageProductCacheKey(
    runtimeIdentity: string,
    source: HomepageCollectionSource,
    normalizedProductIds: string[] | undefined,
    validProductIds: string[] | undefined,
    pageSize: number,
    seedKey: string
): string {
    if (source !== 'curated') {
        return `${runtimeIdentity}:homepage:${source}:${pageSize}:${seedKey}`;
    }

    if (validProductIds && validProductIds.length > 0) {
        return `${runtimeIdentity}:homepage:curated:${pageSize}:${validProductIds.join(',')}`;
    }

    if (normalizedProductIds?.length === 0) {
        return `${runtimeIdentity}:homepage:curated-empty`;
    }

    return `${runtimeIdentity}:homepage:curated-invalid`;
}

export function refreshHomepageSectionProductsCache({
    productIds,
    pageSize = 8,
    source,
    seedKey = 'homepage-products',
    force = true,
}: HomepageSectionProductsRefreshOptions = {}): Promise<ProductListItemDto[]> {
    const normalizedProductIds = normalizeHomepageProductIds(productIds);
    const validProductIds = normalizedProductIds
        ? getValidProductIds(normalizedProductIds)
        : undefined;
    const normalizedSource = resolveHomepageCollectionSource(source, productIds);
    const runtimeIdentity = getRuntimeCacheIdentity('homepage-products', {
        includeAffiliate: true,
        includeLanguage: true,
    });
    const cacheKey = createHomepageProductCacheKey(
        runtimeIdentity,
        normalizedSource,
        normalizedProductIds,
        validProductIds,
        pageSize,
        seedKey
    );

    if (cacheKey.endsWith(':homepage:curated-invalid')
        || cacheKey.endsWith(':homepage:curated-empty')) {
        return Promise.resolve([]);
    }

    return loadProductsForKey(
        cacheKey,
        normalizedSource,
        validProductIds,
        pageSize,
        seedKey,
        { force }
    );
}

export function invalidateHomepageSectionProductsCache(): void {
    productCache.clear();
}

export function useHomepageSectionProducts({
    productIds,
    pageSize = 8,
    source,
    seedKey = 'homepage-products',
}: UseHomepageSectionProductsOptions = {}): UseHomepageSectionProductsState {
    const mountedRef = useRef(true);
    const language = useLanguageStore((state) => state.language);
    const runtimeIdentity = getRuntimeCacheIdentity('homepage-products', {
        includeAffiliate: true,
        includeLanguage: true,
    });
    const normalizedSource = useMemo(
        () => resolveHomepageCollectionSource(source, productIds),
        [productIds, source]
    );
    const normalizedProductIdsIdentity = getHomepageProductIdsIdentity(productIds);
    const normalizedProductIds = useMemo<string[] | undefined>(() => {
        const normalized = JSON.parse(normalizedProductIdsIdentity) as string[] | null;
        return normalized ?? undefined;
    }, [normalizedProductIdsIdentity]);
    const validProductIds = useMemo(
        () => normalizedProductIds ? getValidProductIds(normalizedProductIds) : undefined,
        [normalizedProductIds]
    );

    const cacheKey = useMemo(() => {
        return createHomepageProductCacheKey(
            runtimeIdentity,
            normalizedSource,
            normalizedProductIds,
            validProductIds,
            pageSize,
            seedKey
        );
    }, [language, normalizedProductIds, normalizedSource, pageSize, runtimeIdentity, seedKey, validProductIds]);

    const deferredCacheKey = useDeferredValue(cacheKey);
    const [products, setProducts] = useState<ProductListItemDto[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const requestSequenceRef = useRef(0);

    const loadProducts = useCallback(async (forceRefresh = false) => {
        const requestSequence = ++requestSequenceRef.current;

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

        setIsLoading(true);
        setError(null);

        try {
            const result = await loadProductsForKey(
                deferredCacheKey,
                normalizedSource,
                validProductIds,
                pageSize,
                seedKey,
                { force: forceRefresh }
            );

            if (mountedRef.current && requestSequenceRef.current === requestSequence) {
                setProducts(result);
                setError(null);
            }
        } catch (err) {
            console.error('[useHomepageSectionProducts] Failed to fetch homepage products:', err);
            if (mountedRef.current && requestSequenceRef.current === requestSequence) {
                setProducts([]);
                setError('Failed to load homepage products');
            }
        } finally {
            if (mountedRef.current && requestSequenceRef.current === requestSequence) {
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
    invalidateHomepageSectionProductsCache();
}

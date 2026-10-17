/**
 * useVariantSelector Hook
 * 
 * PHASE 11.3 — STOREFRONT VARIANT SELECTION
 * 
 * Manages variant option candidate state for product pages.
 * Responsibilities:
 * - Extract all attribute groups from variants
 * - Track selected option candidates
 * - Ask backend authority to resolve variant identity
 */

import { useState, useMemo, useCallback, useEffect } from 'react';
import type { ProductPricingPresentationDto, ProductReadDto, ProductVariantDto } from '@/types';
import { resolveProductImage } from '@/utils';
import { resolveProductVariant } from '@/services/api/storefrontApi';

/**
 * Attribute group with all possible values
 */
export interface AttributeGroup {
    name: string;
    values: string[];
}

/**
 * Selected attributes map { attributeName: selectedValue }
 */
export type SelectedAttributes = Record<string, string>;

/**
 * Result of the variant selector hook
 */
export interface VariantSelectorResult {
    /** All attribute groups with their possible values */
    attributeGroups: AttributeGroup[];
    /** Currently selected attributes */
    selectedAttributes: SelectedAttributes;
    /** The currently selected/active variant */
    selectedVariant: ProductVariantDto | null;
    /** Whether backend variant resolution is in flight */
    isResolvingVariant: boolean;
    /** Whether the current option candidates failed backend resolution */
    hasVariantResolutionError: boolean;
    /** Whether the selected variant is in stock */
    isInStock: boolean;
    /** Select a specific attribute value */
    selectAttribute: (attributeName: string, value: string) => void;
    /** Option values are candidates only; backend validates combinations. */
    isValueAvailable: (attributeName: string, value: string) => boolean;
    /** Product has multiple variants */
    hasVariants: boolean;
    /** The active image (variant or product fallback) */
    activeImage: string | null;
    /** Preferred product media ID for the selected variant */
    preferredMediaId: string | null;
    /** Backend-composed pricing presentation for the selected variant/product */
    pricing: ProductPricingPresentationDto;
    /** Whether the product has an active promotion */
    hasActivePromotion: boolean;
    /** The promotion badge text */
    promotionBadge: string | null;
}

/**
 * Hook for managing variant selection on product pages.
 * 
 * Usage:
 * ```tsx
 * const { 
 *   attributeGroups, 
 *   selectedAttributes,
 *   selectedVariant,
 *   selectAttribute,
 *   effectivePrice
 * } = useVariantSelector(product);
 * ```
 */
export function useVariantSelector(product: ProductReadDto | null): VariantSelectorResult {
    const [selectedAttributes, setSelectedAttributes] = useState<SelectedAttributes>({});
    const [selectedVariant, setSelectedVariant] = useState<ProductVariantDto | null>(null);
    const [isResolvingVariant, setIsResolvingVariant] = useState(false);
    const [hasVariantResolutionError, setHasVariantResolutionError] = useState(false);

    // Extract unique attribute groups from all variants
    const attributeGroups = useMemo<AttributeGroup[]>(() => {
        if (!product?.variants || product.variants.length === 0) {
            return [];
        }

        // Collect all unique attribute names and their values
        const attributeMap = new Map<string, Set<string>>();

        product.variants.forEach((variant) => {
            variant.attributes.forEach((attr) => {
                if (!attributeMap.has(attr.name)) {
                    attributeMap.set(attr.name, new Set());
                }
                attributeMap.get(attr.name)!.add(attr.value);
            });
        });

        // Convert to array format
        return Array.from(attributeMap.entries()).map(([name, values]) => ({
            name,
            values: Array.from(values),
        }));
    }, [product?.variants]);

    useEffect(() => {
        setSelectedAttributes({});
        setSelectedVariant(null);
        setHasVariantResolutionError(false);
    }, [product?.id]);

    useEffect(() => {
        if (!product?.id || !product.hasVariants) {
            setSelectedVariant(null);
            setIsResolvingVariant(false);
            setHasVariantResolutionError(false);
            return;
        }

        const controller = new AbortController();
        setSelectedVariant(null);
        setIsResolvingVariant(true);
        setHasVariantResolutionError(false);

        resolveProductVariant(
            product.id,
            { selectedOptions: selectedAttributes },
            controller.signal
        )
            .then((response) => {
                if (controller.signal.aborted) return;
                setSelectedVariant(response.variant);
                setHasVariantResolutionError(false);
            })
            .catch((error: unknown) => {
                if (controller.signal.aborted) {
                    return;
                }
                console.warn('[useVariantSelector] Backend variant resolution failed', error);
                setSelectedVariant(null);
                setHasVariantResolutionError(true);
            })
            .finally(() => {
                if (!controller.signal.aborted) {
                    setIsResolvingVariant(false);
                }
            });

        return () => controller.abort();
    }, [product?.id, product?.hasVariants, selectedAttributes]);

    const isValueAvailable = useCallback((): boolean => true, []);

    // Select an attribute value
    const selectAttribute = useCallback(
        (attributeName: string, value: string) => {
            setSelectedAttributes((prev) => ({
                ...prev,
                [attributeName]: value,
            }));
        },
        []
    );

    // Computed values
    const isInStock = selectedVariant?.inStock ?? false;

    // PHASE 11.6: Use product.hasVariants from API (respects PostgreSQL HasVariants flag)
    // DO NOT compute locally as variants.length > 1 - this misses edge cases
    // The API's hasVariants flag is the source of truth for whether variant selection is required
    const hasVariants = product?.hasVariants ?? false;

    const preferredMediaId = selectedVariant?.mediaId ?? null;

    // Active image: variant image, matched media image, or first product image
    const activeImage = useMemo(
        () => resolveProductImage(product, selectedVariant),
        [product, selectedVariant]
    );

    const pricing = selectedVariant?.pricing ?? product?.pricing ?? { currentPrice: 0 };
    const hasActivePromotion = product?.hasActivePromotion ?? false;
    const promotionBadge = pricing.discountBadge ?? product?.promotionBadge ?? null;

    return {
        attributeGroups,
        selectedAttributes,
        selectedVariant,
        isResolvingVariant,
        hasVariantResolutionError,
        isInStock,
        selectAttribute,
        isValueAvailable,
        hasVariants,
        activeImage,
        preferredMediaId,
        pricing,
        hasActivePromotion,
        promotionBadge,
    };
}

export default useVariantSelector;

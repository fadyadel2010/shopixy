import type { ProductReadDto, ProductVariantDto, StorefrontVariantResolutionRequest, StorefrontVariantResolutionResponse } from '@/types';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const money = (value: unknown) => typeof value === 'number' && Number.isFinite(value) && value >= 0;
const identity = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value);
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0;

function pricing(value: unknown): boolean {
    return record(value) && money(value.currentPrice) &&
        [value.compareAtPrice, value.discountedPrice].every(amount => amount == null || money(amount));
}
function availability(value: unknown): boolean {
    return record(value) && ['isAvailable', 'canPurchase', 'canAddToCart', 'canBuyNow'].every(key => typeof value[key] === 'boolean') &&
        text(value.stockLabel) && (value.maximumQuantity == null || money(value.maximumQuantity));
}
function variant(value: unknown): value is ProductVariantDto {
    return record(value) && identity(value.id) && value.isActive === true && pricing(value.pricing) && availability(value.availability) &&
        Array.isArray(value.attributes) && value.attributes.every(attribute => record(attribute) && text(attribute.name) && text(attribute.value));
}

// Admission validates the contract; it never fills a missing price, invents
// stock/purchase permission, or calculates a promotion. Optional narrative/media
// absence remains absence. Return the original backend object, not a local model.
export function requireProductDetailAuthority(value: unknown, identifier: string): ProductReadDto {
    if (!record(value) || !identity(value.id) || !text(value.name) || !pricing(value.pricing) || !availability(value.availability) ||
        typeof value.hasVariants !== 'boolean' || !Array.isArray(value.variants) || !value.variants.every(variant) ||
        !Array.isArray(value.media) || !Array.isArray(value.images) ||
        (GUID.test(identifier) ? value.id.toLowerCase() !== identifier.toLowerCase() : typeof value.slug !== 'string' || value.slug.toLowerCase() !== identifier.trim().toLowerCase())) {
        throw new Error('PRODUCT_RESPONSE_AUTHORITY_FAILED');
    }
    return value as unknown as ProductReadDto;
}

export function requireVariantResolutionAuthority(value: unknown, productId: string, request: StorefrontVariantResolutionRequest): StorefrontVariantResolutionResponse {
    if (!record(value) || typeof value.productId !== 'string' || value.productId.toLowerCase() !== productId.toLowerCase() ||
        !variant(value.variant) || value.resolvedVariantId !== value.variant.id) {
        throw new Error('VARIANT_RESPONSE_AUTHORITY_FAILED');
    }
    const resolvedVariant = value.variant;
    const candidates = Object.entries(request.selectedOptions);
    // Empty candidates ask the backend for its default; never choose one here.
    if (candidates.length && (resolvedVariant.attributes.length !== candidates.length || !candidates.every(([name, selected]) =>
        resolvedVariant.attributes.some(attribute => attribute.name.toLowerCase() === name.trim().toLowerCase() && attribute.value.toLowerCase() === selected.trim().toLowerCase())))) {
        throw new Error('VARIANT_RESPONSE_AUTHORITY_FAILED');
    }
    return value as unknown as StorefrontVariantResolutionResponse;
}

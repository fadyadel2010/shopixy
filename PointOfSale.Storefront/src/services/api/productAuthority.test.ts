import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fetchProductByIdentifier, resolveProductVariant } from './storefrontApi';
import { httpClient } from './httpClient';

vi.mock('./httpClient', () => ({ httpClient: { get: vi.fn(), post: vi.fn() } }));

const productId = '11111111-1111-4111-8111-111111111111';
const variantId = '22222222-2222-4222-8222-222222222222';
const availability = { isAvailable: true, canPurchase: false, canAddToCart: false, canBuyNow: false, stockLabel: 'Unavailable', maximumQuantity: 0 };
function product() {
    return { id: productId, slug: 'authoritative', name: 'Backend name', description: 'Backend narrative', categoryName: 'Backend category',
        price: 123, effectivePrice: 123, pricing: { currentPrice: 119, compareAtPrice: 123, discountBadge: 'Backend badge' }, availability,
        hasVariants: true, variants: [], media: [], images: [], isAvailable: true, inStock: false, stockQuantity: 0 };
}
function resolution() {
    return { productId, resolvedVariantId: variantId, variant: { id: variantId, isActive: true, inStock: false, totalStock: 0,
        price: 123, effectivePrice: 123, pricing: { currentPrice: 119 }, availability, attributes: [{ name: 'Color', value: 'Red' }] } };
}
describe('product and variant response authority admission', () => {
    beforeEach(() => vi.clearAllMocks());
    it('passes backend product truth unchanged, including unavailable state, empty media and missing optional narrative', async () => {
        const value = { ...product(), description: undefined };
        vi.mocked(httpClient.get).mockResolvedValueOnce({ data: value });
        expect(await fetchProductByIdentifier('authoritative')).toBe(value);
    });
    it.each([
        null, {}, { ...product(), id: 'foreign' }, { ...product(), slug: 'foreign' },
        { ...product(), name: '' }, { ...product(), pricing: undefined }, { ...product(), pricing: { currentPrice: Number.NaN } },
        { ...product(), availability: undefined }, { ...product(), variants: undefined },
    ])('rejects incomplete or mismatched product authority instead of inventing fields: %j', async data => {
        vi.mocked(httpClient.get).mockResolvedValueOnce({ data });
        await expect(fetchProductByIdentifier('authoritative')).rejects.toThrow('PRODUCT_RESPONSE_AUTHORITY_FAILED');
    });
    it('rejects a different product under an ID route', async () => {
        vi.mocked(httpClient.get).mockResolvedValueOnce({ data: { ...product(), id: variantId } });
        await expect(fetchProductByIdentifier(productId)).rejects.toThrow('PRODUCT_RESPONSE_AUTHORITY_FAILED');
    });
    it.each([404, 403, 503])('does not fabricate a product after HTTP %s rejection', async status => {
        const error = { response: { status } };
        vi.mocked(httpClient.get).mockRejectedValueOnce(error);
        await expect(fetchProductByIdentifier('authoritative')).rejects.toBe(error);
    });
    it('sends only option candidates and accepts backend pricing/denial rather than computing from the candidate', async () => {
        const value = resolution();
        vi.mocked(httpClient.post).mockResolvedValueOnce({ data: value });
        const request = { selectedOptions: { Color: 'Red' }, price: 1, variantId: 'invented', availability: { canPurchase: true } };
        expect(await resolveProductVariant(productId, request)).toBe(value);
        expect(httpClient.post).toHaveBeenCalledWith(expect.any(String), { selectedOptions: { Color: 'Red' } }, expect.any(Object));
        expect(value.variant.availability.canPurchase).toBe(false);
    });
    it.each([
        null, {}, { ...resolution(), productId: variantId }, { ...resolution(), resolvedVariantId: productId },
        { ...resolution(), variant: { ...resolution().variant, pricing: undefined } },
        { ...resolution(), variant: { ...resolution().variant, availability: undefined } },
        { ...resolution(), variant: { ...resolution().variant, isActive: false } },
        { ...resolution(), variant: { ...resolution().variant, attributes: [{ name: 'Color', value: 'Blue' }] } },
    ])('rejects foreign, incomplete or option-mismatched variant authority: %j', async data => {
        vi.mocked(httpClient.post).mockResolvedValueOnce({ data });
        await expect(resolveProductVariant(productId, { selectedOptions: { Color: 'Red' } })).rejects.toThrow('VARIANT_RESPONSE_AUTHORITY_FAILED');
    });
});

import { describe, expect, it } from 'vitest';
import { normalizeCartResponse } from './cartResponse';
import type { CartDto } from '@/types';

function makeRawCart(items: unknown[]): CartDto {
    return {
        cartId: 'cart-a',
        tenantId: 'tenant-a',
        ownerType: 'session',
        ownerId: 'session-a',
        items,
        itemCount: items.reduce<number>(
            (sum, item) => sum + Number((item as { quantity?: number }).quantity ?? 0),
            0
        ),
        subtotal: 300,
        discountTotal: 0,
        taxTotal: 0,
        grandTotal: 300,
        currency: 'USD',
        lastUpdatedUtc: '2026-08-12T00:00:00.000Z',
        expiresAtUtc: '2026-08-13T00:00:00.000Z',
        appliedCouponCode: null,
        appliedCoupon: null,
    } as CartDto;
}

describe('storefront cart API response normalization', () => {
    it('maps authoritative backend cart item presentation fields into the Storefront cart contract', () => {
        const normalized = normalizeCartResponse(makeRawCart([
            {
                productId: 'product-1',
                variantId: 'variant-red-xl',
                name: 'Authoritative Jacket',
                nameAr: 'Arabic Jacket',
                sku: 'JACKET-RED-XL',
                unitPrice: 100,
                quantity: 2,
                lineTotal: 200,
                thumbnailImage: 'https://cdn.shopixy.test/products/jacket-red-xl.jpg',
                variantAttributes: [
                    { name: 'Color', value: 'Red' },
                    { name: 'Size', value: 'XL' },
                ],
            },
        ]));

        expect(normalized.items[0]).toMatchObject({
            productName: 'Authoritative Jacket',
            productNameAr: 'Arabic Jacket',
            sku: 'JACKET-RED-XL',
            imageUrl: 'https://cdn.shopixy.test/products/jacket-red-xl.jpg',
            variantAttributes: [
                { name: 'Color', value: 'Red' },
                { name: 'Size', value: 'XL' },
            ],
            quantity: 2,
            unitPrice: 100,
            lineTotal: 200,
        });
    });

    it('keeps server-authoritative pricing and distinct variant lines without recalculation', () => {
        const normalized = normalizeCartResponse(makeRawCart([
            {
                productId: 'product-1',
                variantId: 'variant-red',
                name: 'Variant Product',
                sku: 'SKU-RED',
                unitPrice: 100,
                quantity: 1,
                lineTotal: 100,
                thumbnailImage: 'https://cdn.shopixy.test/red.jpg',
                variantAttributes: [{ name: 'Color', value: 'Red' }],
            },
            {
                productId: 'product-1',
                variantId: 'variant-blue',
                name: 'Variant Product',
                sku: 'SKU-BLUE',
                unitPrice: 125,
                quantity: 3,
                lineTotal: 375,
                thumbnailImage: 'https://cdn.shopixy.test/blue.jpg',
                variantAttributes: [{ name: 'Color', value: 'Blue' }],
            },
        ]));

        expect(normalized.items).toHaveLength(2);
        expect(normalized.items.map((item) => item.variantId)).toEqual(['variant-red', 'variant-blue']);
        expect(normalized.items.map((item) => item.lineTotal)).toEqual([100, 375]);
    });

    it('reflects refreshed and removed server cart lines without preserving stale product data', () => {
        const firstCart = normalizeCartResponse(makeRawCart([
            {
                productId: 'product-1',
                variantId: 'variant-a',
                name: 'Old Product Name',
                sku: 'OLD-SKU',
                unitPrice: 50,
                quantity: 1,
                lineTotal: 50,
                thumbnailImage: 'https://cdn.shopixy.test/old.jpg',
            },
        ]));
        const refreshedCart = normalizeCartResponse({
            ...makeRawCart([]),
            cartId: 'cart-b',
            items: [
                {
                    productId: 'product-2',
                    variantId: 'variant-b',
                    name: 'Fresh Product Name',
                    sku: 'FRESH-SKU',
                    unitPrice: 75,
                    quantity: 2,
                    lineTotal: 150,
                    thumbnailImage: 'https://cdn.shopixy.test/fresh.jpg',
                },
            ],
            itemCount: 2,
        } as unknown as CartDto);

        expect(firstCart.items[0].productName).toBe('Old Product Name');
        expect(refreshedCart.cartId).toBe('cart-b');
        expect(refreshedCart.items).toHaveLength(1);
        expect(refreshedCart.items[0]).toMatchObject({
            productId: 'product-2',
            productName: 'Fresh Product Name',
            sku: 'FRESH-SKU',
        });
    });

    it('uses an empty image value only when the authoritative response has no image', () => {
        const normalized = normalizeCartResponse(makeRawCart([
            {
                productId: 'product-no-image',
                name: 'No Image Product',
                sku: 'NO-IMAGE',
                unitPrice: 10,
                quantity: 1,
                lineTotal: 10,
                thumbnailImage: '   ',
            },
        ]));

        expect(normalized.items[0].imageUrl).toBeUndefined();
        expect(normalized.items[0].productName).toBe('No Image Product');
    });
});

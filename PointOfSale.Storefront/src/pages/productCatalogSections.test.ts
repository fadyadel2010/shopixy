import { describe, expect, it } from 'vitest';
import { splitProductCatalogSections } from './productCatalogSections';

describe('product catalog section insertion points', () => {
    it('supports products without configured sections', () => {
        expect(splitProductCatalogSections()).toEqual({ top: [], bottom: [] });
    });

    it('keeps configured product sections as catalog content by default', () => {
        const sections = splitProductCatalogSections([
            {
                id: 'popular-products',
                type: 'product-grid',
                order: 0,
                enabled: true,
                settings: { title: 'Popular Now' },
            },
        ]);

        expect(sections.top).toEqual([]);
        expect(sections.bottom.map((section) => section.id)).toEqual(['popular-products']);
    });

    it('sorts and splits multiple configured sections into canonical slots', () => {
        const sections = splitProductCatalogSections([
            {
                id: 'after-catalog',
                type: 'featured-products',
                order: 3,
                enabled: true,
                settings: { title: 'Cart Ready Picks', catalogSlot: 'bottom' },
            },
            {
                id: 'disabled',
                type: 'product-grid',
                order: 0,
                enabled: false,
                settings: { title: 'Disabled' },
            },
            {
                id: 'before-catalog',
                type: 'category-grid',
                order: 2,
                enabled: true,
                settings: { title: 'Fast Browsing', catalogPlacement: 'before-catalog' },
            },
            {
                id: 'second-before-catalog',
                type: 'banner',
                order: 1,
                enabled: true,
                settings: { title: 'Top Offer', placement: 'top' },
            },
        ]);

        expect(sections.top.map((section) => section.id)).toEqual(['second-before-catalog', 'before-catalog']);
        expect(sections.bottom.map((section) => section.id)).toEqual(['after-catalog']);
    });
});

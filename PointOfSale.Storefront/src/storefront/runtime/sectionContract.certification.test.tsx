import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it, vi } from 'vitest';
import type { ProductGridPresentationModel, ProductPresentationModel } from '@/storefront/presentation/models';
import {
    getSectionDefaultSettings,
    materializeSectionSettings,
    sectionContracts,
    validateSectionSettingsStrict,
} from './sectionContract';
import { normalizeSections } from '@/components/sections/homepageSections';

vi.mock('@/components/common/AffiliateLink', () => ({
    AffiliateLink: ({ children }: { children: React.ReactNode }) => children,
}));

vi.mock('@/components/data', () => ({
    DataEmptyState: () => null,
    SkeletonGrid: () => null,
}));

vi.mock('@/hooks/useAffiliateNavigation', () => ({
    useBuildAffiliateUrl: () => (path: string) => path,
}));

import FeaturedProducts from '@/components/sections/defaults/featured-products';

const productGridModel: ProductGridPresentationModel = {
    title: 'Featured Products',
    layout: 'grid',
    pageSize: 2,
    products: [{ id: 'product-1' }, { id: 'product-2' }],
    isLoading: false,
    error: null,
    isCuratedConfig: false,
    isCuratedSelectionInvalid: false,
    emptyStateTitle: 'No products',
    labels: {
        eyebrowProducts: 'Products',
        viewAll: 'View all',
        failedToLoad: 'Failed to load',
    },
};

function renderProductCard(product: ProductPresentationModel) {
    return <article data-product-id={product.id}>{product.id}</article>;
}

describe('storefront runtime section contract certification', () => {
    it('accepts every canonical default settings payload strictly', () => {
        for (const contract of sectionContracts) {
            expect(() => validateSectionSettingsStrict(contract.type, contract.defaultSettings)).not.toThrow();
            expect(materializeSectionSettings(contract.type, contract.defaultSettings)).toEqual(contract.defaultSettings);
        }
    });

    it('fails unknown, missing, and enum-mismatched properties', () => {
        const defaults = getSectionDefaultSettings('featured-products');

        expect(() => validateSectionSettingsStrict('featured-products', {
            ...defaults,
            unsupportedSetting: true,
        })).toThrow(/Unknown setting/);

        const missingDisplayStyle = { ...defaults };
        delete missingDisplayStyle.displayStyle;
        expect(() => validateSectionSettingsStrict('featured-products', missingDisplayStyle)).toThrow(/Missing setting "displayStyle"/);

        expect(() => validateSectionSettingsStrict('featured-products', {
            ...defaults,
            displayStyle: 'slider',
        })).toThrow(/invalid enum value/);
    });

    it('rejects legacy aliases instead of repairing persisted layouts during runtime', () => {
        expect(() => validateSectionSettingsStrict('products', {
            title: 'Legacy Products',
            productSource: 'all',
            productIds: [],
            columns: 4,
            rows: 2,
            showPrice: true,
            showRating: true,
            showAddToCart: true,
        })).toThrow(/Unknown section type/);

        expect(() => validateSectionSettingsStrict('product-grid', {
            title: 'Legacy Products',
            productSource: 'all',
            productIds: [],
            columns: 4,
            rows: 2,
            showPrice: true,
            showRating: true,
            showAddToCart: true,
        })).toThrow(/Unknown setting "productSource"/);
    });

    it('skips unrecoverable invalid sections without dropping valid sections', () => {
        const validSettings = getSectionDefaultSettings('featured-products');
        const normalized = normalizeSections([
            {
                id: 'valid-featured',
                type: 'featured-products',
                order: 1,
                enabled: true,
                settings: validSettings,
            },
            {
                id: 'invalid-featured',
                type: 'featured-products',
                order: 0,
                enabled: true,
                settings: {
                    ...validSettings,
                    displayStyle: 'slider',
                },
            },
        ]);

        expect(normalized).toHaveLength(1);
        expect(normalized[0].id).toBe('valid-featured');
    });

    it('skips an invalid single section before runtime materialization can terminate the page', () => {
        const validSettings = getSectionDefaultSettings('featured-products');
        const invalidSettings = { ...validSettings };
        delete invalidSettings.displayStyle;

        const normalized = normalizeSections([{
            id: 'invalid-single-featured',
            type: 'featured-products',
            order: 0,
            enabled: true,
            settings: invalidSettings,
        }]);

        expect(normalized).toEqual([]);

        const sectionRendererSource = readFileSync(
            resolve(process.cwd(), 'src/components/sections/SectionRenderer.tsx'),
            'utf8'
        );
        expect(sectionRendererSource).toContain('return normalizeSections([section]);');
    });

    it('certifies displayStyle changes final rendered layout without alias fallback', () => {
        const gridMarkup = renderToStaticMarkup(
            <FeaturedProducts
                productGridModel={{ ...productGridModel, layout: 'grid' }}
                renderProductCard={renderProductCard}
                displayStyle="grid"
                showBadge
                badgeText="Featured"
            />
        );
        const carouselMarkup = renderToStaticMarkup(
            <FeaturedProducts
                productGridModel={{ ...productGridModel, layout: 'carousel' }}
                renderProductCard={renderProductCard}
                displayStyle="carousel"
                showBadge
                badgeText="Featured"
            />
        );

        expect(gridMarkup).toContain('grid grid-cols-2 md:grid-cols-4 gap-6');
        expect(gridMarkup).not.toContain('overflow-x-auto pb-2');
        expect(carouselMarkup).toContain('flex gap-6 overflow-x-auto pb-2');
        expect(carouselMarkup).toContain('min-width:240px');
    });
});

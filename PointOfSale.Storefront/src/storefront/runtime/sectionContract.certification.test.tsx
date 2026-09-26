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

vi.mock('@/utils/htmlSanitizer', () => ({
    sanitizeRichTextHtml: (value?: string) => value || '',
}));

import FeaturedProducts from '@/components/sections/defaults/featured-products';
import ImageBlock from '@/components/sections/defaults/image-block';
import TextBlock from '@/components/sections/defaults/text-block';
import VideoBlock from '@/components/sections/defaults/video-block';

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

    it('renders backend-generated GUIDs as valid curated storefront identities', () => {
        expect(() => validateSectionSettingsStrict('featured-products', {
            ...getSectionDefaultSettings('featured-products'),
            source: 'curated',
            productIds: ['d4807be1-bf1f-d3e8-f1e8-53afe614d11d'],
        })).not.toThrow();
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

    it('maps every editorial media contract field to visible presentation', () => {
        const textMarkup = renderToStaticMarkup(
            <TextBlock heading="Editorial heading" body="<strong>Editorial body</strong>" alignment="right" maxWidth="narrow" />
        );
        const imageMarkup = renderToStaticMarkup(
            <ImageBlock
                imageUrl="/media/editorial.jpg"
                altText="Editorial alt"
                caption="Editorial caption"
                aspectRatio="21:9"
                maxWidth="full"
            />
        );
        const videoMarkup = renderToStaticMarkup(
            <VideoBlock
                title="Editorial video"
                videoUrl="/media/editorial.mp4"
                autoplay={false}
                showControls
                aspectRatio="4:3"
            />
        );

        expect(textMarkup).toContain('Editorial heading');
        expect(textMarkup).toContain('<strong>Editorial body</strong>');
        expect(textMarkup).toContain('text-align:right');
        expect(textMarkup).toContain('max-w-2xl');
        expect(imageMarkup).toContain('src="/media/editorial.jpg"');
        expect(imageMarkup).toContain('alt="Editorial alt"');
        expect(imageMarkup).toContain('Editorial caption');
        expect(imageMarkup).toContain('aspect-ratio:21/9');
        expect(imageMarkup).toContain('max-w-none');
        expect(videoMarkup).toContain('Editorial video');
        expect(videoMarkup).toContain('src="/media/editorial.mp4"');
        expect(videoMarkup).toContain('aspect-ratio:4/3');
        expect(videoMarkup).toContain('controls=""');
        expect(videoMarkup).not.toContain('autoplay=""');
    });

    it('rejects malformed colors and whitespace-bearing media URLs', () => {
        expect(() => validateSectionSettingsStrict('hero-banner', {
            ...getSectionDefaultSettings('hero-banner'),
            backgroundColor: 'red',
        })).toThrow(/hex color/);

        expect(() => validateSectionSettingsStrict('video-block', {
            ...getSectionDefaultSettings('video-block'),
            videoUrl: 'https://example.com/not encoded.mp4',
        })).toThrow(/safe URL/);
    });

    it('suppresses product shelf View All when the runtime model omits a catalog href', () => {
        const visibleMarkup = renderToStaticMarkup(
            <FeaturedProducts
                productGridModel={{ ...productGridModel, viewAllHref: '/products' }}
                renderProductCard={renderProductCard}
                displayStyle="grid"
            />
        );
        const suppressedMarkup = renderToStaticMarkup(
            <FeaturedProducts
                productGridModel={{ ...productGridModel, viewAllHref: null }}
                renderProductCard={renderProductCard}
                displayStyle="grid"
            />
        );

        expect(visibleMarkup).toContain('View all');
        expect(suppressedMarkup).not.toContain('View all');
    });

    it('keeps themed product shelf actions model-owned instead of hardcoding the catalog path', () => {
        const themeProductGridSources = [
            'src/storefront/themes/modern/sections/product-grid.tsx',
            'src/storefront/themes/minimal/sections/product-grid.tsx',
            'src/storefront/themes/cosmetics/sections/product-grid.tsx',
            'src/storefront/themes/electronics/sections/product-grid.tsx',
            'src/components/sections/defaults/product-grid.tsx',
            'src/components/sections/defaults/featured-products.tsx',
            'src/components/sections/defaults/related-products.tsx',
        ].map((relativePath) => readFileSync(resolve(process.cwd(), relativePath), 'utf8'));

        for (const source of themeProductGridSources) {
            expect(source).toContain('model.viewAllHref');
            expect(source).not.toContain("href: '/products'");
            expect(source).not.toContain('href="/products"');
        }
    });
});

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const productListPageSource = readFileSync(
    resolve(process.cwd(), 'src/pages/ProductListPage.tsx'),
    'utf8'
);

describe('ProductListPage catalog ownership certification', () => {
    it('does not let configured sections replace the canonical catalog runtime', () => {
        expect(productListPageSource).not.toContain('productSections.length > 0 ?');
        expect(productListPageSource).toContain('data-catalog-owner="ProductListPage"');
        expect(productListPageSource).toContain('renderConfiguredProductSections(topProductSections,');
        expect(productListPageSource).toContain('renderConfiguredProductSections(bottomProductSections,');
        expect(productListPageSource).toContain('sectionProps={{ suppressActionHref: PRODUCT_CATALOG_PATH }}');
    });

    it('keeps every canonical catalog control in ProductListPage', () => {
        const canonicalControls = [
            '<SearchInput',
            '<SortSelector',
            '<MobileFilterButton',
            '<CategoryNav',
            '<ProductFiltersSidebar',
            '<ActiveFiltersBar',
            '<Pagination',
            '<CommercePageHeader',
            '<ProductCard',
            '<SkeletonGrid',
            '<CommerceEmptyState',
            'totalCount',
            'useProductListDiscovery({ routeCategory })',
        ];

        for (const control of canonicalControls) {
            expect(productListPageSource).toContain(control);
        }
    });

    it('keeps Home and Product Details on their existing section ownership paths', () => {
        const homePageSource = readFileSync(resolve(process.cwd(), 'src/pages/HomePage.tsx'), 'utf8');
        const productDetailsSource = readFileSync(resolve(process.cwd(), 'src/pages/ProductDetailsPage.tsx'), 'utf8');

        expect(homePageSource).toContain('<SectionRenderer sections={homeSections}');
        expect(productDetailsSource).toContain('resolveProductTemplate(');
        expect(productDetailsSource).toContain('<SectionRenderer');
        expect(productDetailsSource).toContain('sectionProps={{');
        expect(productDetailsSource).toContain('product,');
    });
});

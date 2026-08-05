import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const read = (relativePath: string) => readFileSync(resolve(process.cwd(), relativePath), 'utf8');

describe('Phase 1 runtime ownership recovery', () => {
    it('keeps Catalog and Category pages free of Builder section execution', () => {
        const catalog = read('src/pages/ProductListPage.tsx');
        const category = read('src/pages/CategoryPage.tsx');

        expect(catalog).not.toContain('splitProductCatalogSections');
        expect(catalog).not.toContain('renderConfiguredProductSections');
        expect(category).not.toContain('config.pages.category.sections');
        expect(category).not.toContain('<SectionRenderer');
        expect(category).toContain('data-category-owner="CategoryPage"');
    });

    it('makes shared Search Runtime own enablement, shortcut, and modal behavior', () => {
        const layout = read('src/storefront/layout/StorefrontLayout.tsx');
        const layoutRuntime = read('src/storefront/runtime/layout/LayoutRuntime.tsx');
        const defaultHeader = read('src/components/layout/Header.tsx');
        const themeHeaders = [
            'alizon',
            'cosmetics',
            'fashion',
            'hype',
            'minimal',
            'pearl',
            'phoenix',
            'seven',
            'sharp',
        ].map(theme => read(`src/storefront/themes/${theme}/layout/header.tsx`));

        expect(layout).toContain('<SearchRuntimeProvider>');
        expect(layoutRuntime).toContain('SearchRuntimeTrigger');
        expect(defaultHeader).toContain('SearchSlot');
        expect(defaultHeader).not.toContain('SearchModal');
        expect(defaultHeader).not.toContain('useSearchShortcut');
        expect(defaultHeader).not.toContain('focus=search');
        for (const header of themeHeaders) {
            expect(header).toContain('SearchSlot');
            expect(header).not.toContain('href="/search"');
        }
    });

    it('keeps Product Details implementation runtime-owned', () => {
        const resolver = read('src/storefront/products/ProductTemplateResolver.ts');
        const sectionRenderer = read('src/components/sections/SectionRenderer.tsx');
        const runtimeContainer = read('src/storefront/runtime/containers/RuntimeSectionContainer.tsx');

        expect(resolver).not.toContain('config?.pages?.product?.sections');
        expect(resolver).toContain('injectProductDetailsRuntime');
        expect(sectionRenderer).toContain('PRODUCT_DETAILS_RUNTIME_SECTIONS');
        expect(runtimeContainer).toContain('const showVariantSelector = true');
        expect(runtimeContainer).toContain('const showAddToCart = true');
        expect(runtimeContainer).toContain('const showStockStatus = true');
    });

    it('passes every owned URL filter to Catalog Discovery', () => {
        const adapter = read('src/hooks/useProductListDiscovery.ts');
        const api = read('src/services/api/storefrontApi.ts');

        expect(adapter).toContain('hasVariants: filters.hasVariants');
        expect(adapter).toContain('attribute: filters.attributes.length > 0');
        expect(api).toContain("params.append('hasVariants'");
        expect(api).toContain("params.append('attribute'");
    });
});

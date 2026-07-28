import { readFileSync } from 'fs';
import { join } from 'path';
import { describe, expect, it } from 'vitest';
import { normalizeThemeComponentTheme } from './themeComponentTypes';

const storefrontRoot = join(__dirname, '..', '..');
const read = (path: string) => readFileSync(join(storefrontRoot, path), 'utf8');

const themeComponentsSrc = read('components/theme-components/ThemeComponents.tsx');
const productRenderersSrc = read('components/products/ProductCardRenderers.tsx');
const productRuntimeSrc = read('components/products/ProductCardRuntime.tsx');
const runtimeSectionContainerSrc = read('storefront/runtime/containers/RuntimeSectionContainer.tsx');
const presentationModelsSrc = read('storefront/presentation/models.ts');
const sectionRendererSrc = read('components/sections/SectionRenderer.tsx');

const themedSectionFiles = [
    'storefront/themes/modern/sections/product-grid.tsx',
    'storefront/themes/minimal/sections/product-grid.tsx',
    'storefront/themes/fashion/sections/product-grid.tsx',
    'storefront/themes/pearl/sections/product-grid.tsx',
    'storefront/themes/phoenix/sections/product-grid.tsx',
    'storefront/themes/hype/sections/product-grid.tsx',
    'storefront/themes/electronics/sections/product-grid.tsx',
    'storefront/themes/modern/sections/category-grid.tsx',
    'storefront/themes/minimal/sections/category-grid.tsx',
    'storefront/themes/fashion/sections/category-grid.tsx',
    'storefront/themes/pearl/sections/category-grid.tsx',
    'storefront/themes/phoenix/sections/category-grid.tsx',
    'storefront/themes/hype/sections/category-grid.tsx',
    'storefront/themes/electronics/sections/category-grid.tsx',
    'storefront/themes/modern/sections/hero-banner.tsx',
    'storefront/themes/minimal/sections/hero-banner.tsx',
    'storefront/themes/fashion/sections/hero-banner.tsx',
    'storefront/themes/pearl/sections/hero-banner.tsx',
    'storefront/themes/phoenix/sections/hero-banner.tsx',
    'storefront/themes/hype/sections/hero-banner.tsx',
] as const;

const homepageRenderOnlySectionFiles = [
    ...themedSectionFiles.filter((file) => file.endsWith('product-grid.tsx') || file.endsWith('category-grid.tsx')),
    'components/sections/defaults/product-grid.tsx',
    'components/sections/defaults/featured-products.tsx',
    'components/sections/defaults/category-grid.tsx',
    'components/sections/defaults/subcategory-grid.tsx',
    'components/sections/defaults/related-products.tsx',
    'components/sections/defaults/product-hero.tsx',
] as const;

const presentationForbiddenRuntimeTerms = [
    "from '@/hooks'",
    "from '@/services",
    "from '@/store'",
    "from '@/store/",
    "from '@/analytics'",
    'useHomepageSectionProducts',
    'useCategories',
    'useProducts',
    'useProductCardRuntime',
    'useCartStore',
    'useWishlist',
    'useAnalytics',
    'fetch(',
    'product.pricing',
] as const;

describe('theme component system architecture', () => {
    it('normalizes current storefront theme ids without registry or resolver coupling', () => {
        expect(normalizeThemeComponentTheme('modern')).toBe('modern');
        expect(normalizeThemeComponentTheme('minimal')).toBe('minimal');
        expect(normalizeThemeComponentTheme('fashion')).toBe('fashion');
        expect(normalizeThemeComponentTheme('pearl')).toBe('pearl');
        expect(normalizeThemeComponentTheme('phoenix')).toBe('phoenix');
        expect(normalizeThemeComponentTheme('hype')).toBe('hype');
        expect(normalizeThemeComponentTheme('electronics')).toBe('electronics');
        expect(normalizeThemeComponentTheme('unknown')).toBe('modern');
        expect(normalizeThemeComponentTheme(null)).toBe('modern');
    });

    it('keeps theme components presentation-only', () => {
        expect(themeComponentsSrc).not.toContain('useCartStore');
        expect(themeComponentsSrc).not.toContain('useWishlist');
        expect(themeComponentsSrc).not.toContain('useCurrency');
        expect(themeComponentsSrc).not.toContain('useStoreConfigStore');
        expect(themeComponentsSrc).not.toContain('@/services');
        expect(themeComponentsSrc).not.toContain('@/hooks');
        expect(themeComponentsSrc).not.toContain('product.pricing');
    });

    it('encodes D1 theme identities inside reusable primitive variants', () => {
        expect(themeComponentsSrc).toContain("minimal: 'border-b border-muted/20");
        expect(themeComponentsSrc).toContain("fashion: 'overflow-hidden bg-background transition-transform");
        expect(themeComponentsSrc).toContain("pearl: 'border border-secondary/30 bg-background shadow-[0_18px_40px");
        expect(themeComponentsSrc).toContain("phoenix: 'overflow-hidden border border-black/10");
        expect(themeComponentsSrc).toContain("hype: 'overflow-hidden rounded-lg border border-muted/20");
        expect(themeComponentsSrc).toContain("electronics: 'overflow-hidden rounded-md border border-muted/25");
        expect(themeComponentsSrc).toContain("title: 'mx-auto mt-4 max-w-2xl font-serif");
        expect(themeComponentsSrc).toContain("electronics: 'font-mono text-lg font-black");
        expect(themeComponentsSrc).toContain('categoryArt.pearl');
        expect(themeComponentsSrc).toContain('categoryArt.phoenix');
        expect(themeComponentsSrc).toContain('categoryArt.hype');
        expect(themeComponentsSrc).toContain('categoryArt.electronics');
        expect(themeComponentsSrc).not.toContain('radial-gradient');
    });

    it('keeps business runtime separate from product-card presentation', () => {
        expect(productRuntimeSrc).toContain('useCurrency');
        expect(productRuntimeSrc).toContain('useCartStore');
        expect(productRuntimeSrc).toContain('WishlistButton');
        expect(productRenderersSrc).toContain("from '@/components/theme-components'");
        expect(productRenderersSrc).not.toContain('useCartStore');
        expect(productRenderersSrc).not.toContain('useCurrency');
        expect(productRenderersSrc).not.toContain('useWishlist');
    });

    it('routes theme sections through reusable themed primitives', () => {
        for (const file of themedSectionFiles) {
            const src = read(file);
            expect(src).toContain("from '@/components/theme-components'");
        }

        expect(read('storefront/themes/modern/sections/category-grid.tsx')).toContain('ThemeCategoryCard');
        expect(read('storefront/themes/fashion/sections/category-grid.tsx')).toContain('ThemeCategoryCard');
        expect(read('storefront/themes/modern/sections/hero-banner.tsx')).toContain('ThemeHeroCTA');
        expect(read('storefront/themes/minimal/sections/product-grid.tsx')).toContain('ThemeSectionHeader');
    });

    it('keeps homepage product/category presentation sections render-only', () => {
        for (const file of homepageRenderOnlySectionFiles) {
            const src = read(file);

            for (const forbiddenTerm of presentationForbiddenRuntimeTerms) {
                expect(src, `${file} leaks runtime term ${forbiddenTerm}`).not.toContain(forbiddenTerm);
            }

            expect(src, `${file} must consume a presentation model`).toContain('PresentationModel');
        }
    });

    it('routes commerce orchestration through runtime containers', () => {
        expect(sectionRendererSrc).toContain('RuntimeSectionContainer');
        expect(runtimeSectionContainerSrc).toContain('useHomepageSectionProducts');
        expect(runtimeSectionContainerSrc).toContain('useCategories');
        expect(runtimeSectionContainerSrc).toContain('useProducts');
        expect(runtimeSectionContainerSrc).toContain('useVariantSelector');
        expect(runtimeSectionContainerSrc).toContain('useCartStore');
        expect(runtimeSectionContainerSrc).toContain('useAnalytics');
        expect(runtimeSectionContainerSrc).toContain('ProductGridPresentationModel');
        expect(runtimeSectionContainerSrc).toContain('CategoryGridPresentationModel');
        expect(runtimeSectionContainerSrc).toContain('ProductHeroPresentationModel');
        expect(runtimeSectionContainerSrc).toContain('renderProductCard');
        expect(runtimeSectionContainerSrc).toContain('<ProductCard');
        expect(runtimeSectionContainerSrc).toContain('ElectronicsProductCardRenderer');
    });

    it('publishes presentation models for runtime-to-theme boundaries', () => {
        expect(presentationModelsSrc).toContain('ProductPresentationModel');
        expect(presentationModelsSrc).toContain('CategoryPresentationModel');
        expect(presentationModelsSrc).toContain('ProductGridPresentationModel');
        expect(presentationModelsSrc).toContain('CategoryGridPresentationModel');
        expect(presentationModelsSrc).toContain('ProductHeroPresentationModel');
        expect(presentationModelsSrc).toContain('RenderProductCard');
    });
});

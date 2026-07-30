import { readFileSync } from 'fs';
import { join } from 'path';
import { describe, expect, it } from 'vitest';
import { resolveProductCardRenderer } from './productCardRendererResolver';

const productDir = __dirname;
const read = (name: string) => readFileSync(join(productDir, name), 'utf8');
const productCardSrc = read('../common/ProductCard.tsx');
const runtimeSrc = read('ProductCardRuntime.tsx');
const renderersSrc = read('ProductCardRenderers.tsx');

describe('product card renderer resolver', () => {
    it('maps resolved storefront themes to theme-specific product-card renderers', () => {
        expect(resolveProductCardRenderer('modern', 'default')).toBe('modern');
        expect(resolveProductCardRenderer('minimal', 'compact')).toBe('minimal');
        expect(resolveProductCardRenderer('fashion', 'detailed')).toBe('fashion');
        expect(resolveProductCardRenderer('pearl', 'compact')).toBe('pearl');
        expect(resolveProductCardRenderer('phoenix', 'compact')).toBe('phoenix');
        expect(resolveProductCardRenderer('hype', 'compact')).toBe('hype');
        expect(resolveProductCardRenderer('alizon', 'compact')).toBe('alizon');
    });

    it('preserves explicit product-level specialized layouts', () => {
        expect(resolveProductCardRenderer('fashion', 'fashion')).toBe('fashion');
        expect(resolveProductCardRenderer('electronics', 'electronics')).toBe('electronics');
    });

    it('falls back deterministically without changing theme resolver or registry', () => {
        expect(resolveProductCardRenderer('unknown-theme', 'default')).toBe('modern');
        expect(resolveProductCardRenderer(null, 'compact')).toBe('modern');
    });
});

describe('product card runtime architecture', () => {
    it('routes through ThemeContext and the shared runtime rather than concrete business-card forks', () => {
        expect(productCardSrc).toContain("from '@/storefront/themes/ThemeContext'");
        expect(productCardSrc).toContain('useProductCardRuntime');
        expect(productCardSrc).toContain('resolveProductCardRenderer');
        expect(productCardSrc).not.toContain('ProductCardDefault,');
        expect(productCardSrc).not.toContain('ProductCardCompact,');
        expect(productCardSrc).not.toContain('ProductCardDetailed,');
    });

    it('keeps price, image, wishlist, and cart behavior in the shared runtime/primitives', () => {
        expect(runtimeSrc).toContain('useCurrency');
        expect(runtimeSrc).toContain('resolveProductImage');
        expect(runtimeSrc).toContain('WishlistButton');
        expect(runtimeSrc).toContain('addItem(product.id, 1)');
        expect(runtimeSrc).toContain('product.displayPriceText');
        expect(runtimeSrc).toContain('product.pricing.compareAtPrice');
        expect(runtimeSrc).toContain('ThemePrice');
        expect(runtimeSrc).toContain('ThemeAddToCartButton');
    });

    it('keeps theme renderers presentation-only', () => {
        expect(renderersSrc).not.toContain('useCurrency');
        expect(renderersSrc).not.toContain('resolveProductImage');
        expect(renderersSrc).not.toContain('useCartStore');
        expect(renderersSrc).not.toContain('useWishlist');
        expect(renderersSrc).not.toContain('product.pricing');
    });

    it('defines distinct visual contracts for every current theme', () => {
        expect(renderersSrc).toContain('data-product-card-theme="modern"');
        expect(renderersSrc).toContain('data-product-card-theme="minimal"');
        expect(renderersSrc).toContain('data-product-card-theme="fashion"');
        expect(renderersSrc).toContain('data-product-card-theme="pearl"');
        expect(renderersSrc).toContain('data-product-card-theme="phoenix"');
        expect(renderersSrc).toContain('data-product-card-theme="hype"');
        expect(renderersSrc).toContain('data-product-card-theme="electronics"');
        expect(renderersSrc).toContain('data-product-card-theme="alizon"');

        expect(renderersSrc).toContain('ThemeSurface');
        expect(renderersSrc).toContain('ThemeProductTitle');
        expect(renderersSrc).toContain('ThemeProductMeta');
        expect(renderersSrc).toContain('ThemeLabel');
    });
});

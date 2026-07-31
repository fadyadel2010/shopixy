import { isValidElement } from 'react';
import { matchRoutes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { ProductDetailsPage, ProductListPage } from '@/pages';
import { ThemePageRoute } from '@/storefront/pages/ThemePageRoute';
import { storefrontRoutes } from './router';

vi.mock('@/analytics', () => ({
    useAnalytics: () => ({
        productView: vi.fn(),
    }),
}));

const getMatchedElementType = (pathname: string) => {
    const matches = matchRoutes(storefrontRoutes, pathname);
    const element = matches?.[matches.length - 1]?.route.element;

    return isValidElement(element) ? element.type : null;
};

describe('storefront product routing', () => {
    it('matches /products to the product listing page', () => {
        expect(getMatchedElementType('/products')).toBe(ProductListPage);
    });

    it('matches /products/:slug direct URLs to the product details page', () => {
        expect(getMatchedElementType('/products/oppo-a6t')).toBe(ProductDetailsPage);
    });

    it('matches refreshed /products/:slug URLs to the product details page', () => {
        expect(getMatchedElementType('/products/oppo-a6t')).toBe(ProductDetailsPage);
    });

    it('matches language-prefixed product details URLs to the product details page', () => {
        expect(getMatchedElementType('/en/products/oppo-a6t')).toBe(ProductDetailsPage);
        expect(getMatchedElementType('/ar/products/oppo-a6t')).toBe(ProductDetailsPage);
    });

    it('keeps category and search routes out of the product details route', () => {
        expect(getMatchedElementType('/category/electronics')).toBe(ProductListPage);
        expect(getMatchedElementType('/search')).toBe(ThemePageRoute);
    });
});

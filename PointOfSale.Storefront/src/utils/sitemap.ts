/**
 * Sitemap Generator
 * Phase 10.1: Multi-language sitemap support
 * 
 * Generates XML sitemap with language alternates (hreflang).
 * Can be used for SSG or dynamic sitemap generation.
 */

import type { SupportedLanguage } from '@/i18n';

// ============================================================================
// Types
// ============================================================================

export interface SitemapEntry {
    /** URL path (without domain or language prefix) */
    path: string;
    /** Last modification date */
    lastmod?: string;
    /** Change frequency */
    changefreq?: 'always' | 'hourly' | 'daily' | 'weekly' | 'monthly' | 'yearly' | 'never';
    /** Priority (0.0 to 1.0) */
    priority?: number;
    /** Available languages for this page */
    languages?: SupportedLanguage[];
}

export interface SitemapConfig {
    /** Base URL of the site (e.g., 'https://store.example.com') */
    baseUrl: string;
    /** Default language */
    defaultLanguage: SupportedLanguage;
    /** All supported languages */
    supportedLanguages: SupportedLanguage[];
}

// ============================================================================
// Static Pages
// ============================================================================

/**
 * Static pages that are always present in the sitemap
 */
export const staticPages: SitemapEntry[] = [
    {
        path: '/',
        changefreq: 'daily',
        priority: 1.0,
        languages: ['en', 'ar'],
    },
    {
        path: '/products',
        changefreq: 'daily',
        priority: 0.9,
        languages: ['en', 'ar'],
    },
    {
        path: '/category',
        changefreq: 'weekly',
        priority: 0.8,
        languages: ['en', 'ar'],
    },
    {
        path: '/cart',
        changefreq: 'always',
        priority: 0.6,
        languages: ['en', 'ar'],
    },
    {
        path: '/search',
        changefreq: 'always',
        priority: 0.7,
        languages: ['en', 'ar'],
    },
];

// ============================================================================
// Sitemap Generation
// ============================================================================

/**
 * Generates XML sitemap with hreflang alternates
 * @param entries - Array of sitemap entries
 * @param config - Sitemap configuration
 * @returns XML string
 */
export function generateSitemap(entries: SitemapEntry[], config: SitemapConfig): string {
    const { baseUrl, defaultLanguage, supportedLanguages } = config;

    const urlElements = entries.map((entry) => {
        const languages = entry.languages || supportedLanguages;

        // Generate hreflang links
        const alternateLinks = languages.map((lang) => {
            const url = lang === defaultLanguage
                ? `${baseUrl}${entry.path}`
                : `${baseUrl}/${lang}${entry.path}`;
            return `    <xhtml:link rel="alternate" hreflang="${lang}" href="${url}" />`;
        }).join('\n');

        // Add x-default
        const xDefaultLink = `    <xhtml:link rel="alternate" hreflang="x-default" href="${baseUrl}${entry.path}" />`;

        // Build URL element
        return `  <url>
    <loc>${baseUrl}${entry.path}</loc>
${alternateLinks}
${xDefaultLink}
    ${entry.lastmod ? `<lastmod>${entry.lastmod}</lastmod>` : ''}
    ${entry.changefreq ? `<changefreq>${entry.changefreq}</changefreq>` : ''}
    ${entry.priority !== undefined ? `<priority>${entry.priority.toFixed(1)}</priority>` : ''}
  </url>`;
    }).join('\n');

    return `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"
        xmlns:xhtml="http://www.w3.org/1999/xhtml">
${urlElements}
</urlset>`;
}

/**
 * Generates a sitemap index for multiple sitemaps
 * @param sitemaps - Array of sitemap URLs
 * @param baseUrl - Base URL
 * @returns XML string
 */
export function generateSitemapIndex(sitemaps: string[], baseUrl: string): string {
    const sitemapElements = sitemaps.map((sitemap) =>
        `  <sitemap>
    <loc>${baseUrl}${sitemap}</loc>
    <lastmod>${new Date().toISOString().split('T')[0]}</lastmod>
  </sitemap>`
    ).join('\n');

    return `<?xml version="1.0" encoding="UTF-8"?>
<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${sitemapElements}
</sitemapindex>`;
}

// ============================================================================
// Dynamic Content Fetchers
// ============================================================================

/**
 * Creates sitemap entries for products
 * @param products - Array of products with slug
 * @returns Sitemap entries
 */
export function createProductEntries(
    products: Array<{ slug: string; updatedAt?: string }>
): SitemapEntry[] {
    return products.map((product) => ({
        path: `/products/${product.slug}`,
        lastmod: product.updatedAt,
        changefreq: 'weekly' as const,
        priority: 0.7,
        languages: ['en', 'ar'],
    }));
}

/**
 * Creates sitemap entries for categories
 * @param categories - Array of categories with slug
 * @returns Sitemap entries
 */
export function createCategoryEntries(
    categories: Array<{ slug: string }>
): SitemapEntry[] {
    return categories.map((category) => ({
        path: `/category/${category.slug}`,
        changefreq: 'weekly' as const,
        priority: 0.8,
        languages: ['en', 'ar'],
    }));
}

/**
 * Creates sitemap entries for CMS pages
 * @param pages - Array of CMS pages with slug
 * @returns Sitemap entries
 */
export function createCmsPageEntries(
    pages: Array<{ slug: string; updatedAt?: string }>
): SitemapEntry[] {
    return pages.map((page) => ({
        path: `/${page.slug}`,
        lastmod: page.updatedAt,
        changefreq: 'monthly' as const,
        priority: 0.5,
        languages: ['en', 'ar'],
    }));
}

// ============================================================================
// Utility Functions
// ============================================================================

/**
 * Gets all sitemap entries including static and dynamic content
 * This is a placeholder - in production, you'd fetch dynamic content from API
 */
export function getAllSitemapEntries(): SitemapEntry[] {
    // Start with static pages
    const entries: SitemapEntry[] = [...staticPages];

    // In production, you would:
    // 1. Fetch all products from API
    // 2. Fetch all categories from API
    // 3. Fetch all CMS pages from API
    // 4. Add their entries

    return entries;
}

/**
 * Default sitemap configuration
 */
export const defaultSitemapConfig: SitemapConfig = {
    baseUrl: typeof window !== 'undefined'
        ? `${window.location.protocol}//${window.location.host}`
        : 'https://example.com',
    defaultLanguage: 'en',
    supportedLanguages: ['en', 'ar'],
};

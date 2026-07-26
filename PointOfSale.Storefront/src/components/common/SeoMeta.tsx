/**
 * SEO Meta Component
 * Phase 10.1: Multi-language Support
 * 
 * Handles dynamic meta tags based on current language:
 * - Language-specific title and description
 * - OpenGraph tags
 * - Canonical URLs with language alternates
 * - hreflang tags for SEO
 */

import { useEffect } from 'react';
import { useTranslation } from '@/i18n';

interface SeoMetaProps {
    title?: string;
    titleAr?: string;
    description?: string;
    descriptionAr?: string;
    image?: string;
    url?: string;
    type?: 'website' | 'article' | 'product';
    noIndex?: boolean;
}

/**
 * Sets document title and meta tags for SEO
 */
export function SeoMeta({
    title,
    titleAr,
    description,
    descriptionAr,
    image,
    url,
    type = 'website',
    noIndex = false,
}: SeoMetaProps) {
    const { language, isRTL } = useTranslation();

    useEffect(() => {
        // Get localized values
        const localizedTitle = language === 'ar' && titleAr ? titleAr : title;
        const localizedDescription = language === 'ar' && descriptionAr ? descriptionAr : description;

        // Set document title
        if (localizedTitle) {
            document.title = localizedTitle;
        }

        // Update or create meta tags
        const updateMeta = (name: string, content: string, isProperty = false) => {
            const attr = isProperty ? 'property' : 'name';
            let meta = document.querySelector(`meta[${attr}="${name}"]`) as HTMLMetaElement;
            if (!meta) {
                meta = document.createElement('meta');
                meta.setAttribute(attr, name);
                document.head.appendChild(meta);
            }
            meta.content = content;
        };

        // Standard meta tags
        if (localizedDescription) {
            updateMeta('description', localizedDescription);
        }

        // OpenGraph tags
        if (localizedTitle) {
            updateMeta('og:title', localizedTitle, true);
        }
        if (localizedDescription) {
            updateMeta('og:description', localizedDescription, true);
        }
        if (image) {
            updateMeta('og:image', image, true);
        }
        if (url) {
            updateMeta('og:url', url, true);
        }
        updateMeta('og:type', type, true);
        updateMeta('og:locale', language === 'ar' ? 'ar_SA' : 'en_US', true);

        // Twitter Card
        updateMeta('twitter:card', 'summary_large_image');
        if (localizedTitle) {
            updateMeta('twitter:title', localizedTitle);
        }
        if (localizedDescription) {
            updateMeta('twitter:description', localizedDescription);
        }
        if (image) {
            updateMeta('twitter:image', image);
        }

        // Robots
        if (noIndex) {
            updateMeta('robots', 'noindex, nofollow');
        }

        // Cleanup function (optional - titles persist intentionally)
    }, [language, title, titleAr, description, descriptionAr, image, url, type, noIndex, isRTL]);

    return null; // This component doesn't render anything
}

/**
 * Hook for setting page title with translation support
 */
export function usePageTitle(titleKey: string, params?: Record<string, string | number>) {
    const { t, language } = useTranslation();

    useEffect(() => {
        document.title = t(titleKey, params);
    }, [t, titleKey, params, language]);
}

/**
 * Component to add hreflang links for multi-language SEO
 */
export function HrefLangLinks({ baseUrl }: { baseUrl: string }) {
    const { language } = useTranslation();

    useEffect(() => {
        // Remove existing hreflang links
        document.querySelectorAll('link[rel="alternate"][hreflang]').forEach(el => el.remove());

        // Add new hreflang links
        const languages = ['en', 'ar'];
        const currentPath = window.location.pathname;

        languages.forEach(lang => {
            const link = document.createElement('link');
            link.rel = 'alternate';
            link.hreflang = lang;
            link.href = `${baseUrl}${currentPath}?lang=${lang}`;
            document.head.appendChild(link);
        });

        // Add x-default
        const defaultLink = document.createElement('link');
        defaultLink.rel = 'alternate';
        defaultLink.hreflang = 'x-default';
        defaultLink.href = `${baseUrl}${currentPath}`;
        document.head.appendChild(defaultLink);

        return () => {
            document.querySelectorAll('link[rel="alternate"][hreflang]').forEach(el => el.remove());
        };
    }, [baseUrl, language]);

    return null;
}

export default SeoMeta;

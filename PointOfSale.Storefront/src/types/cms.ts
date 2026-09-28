export type CmsPageContentType = 'RichText' | 'Blocks';

export interface CmsPage {
    slug: string;
    title: string;
    contentType: CmsPageContentType;
    content: string | CmsPageBlock[];
    schemaVersion: number;
    seo: CmsPageSeo;
    requestedLanguage: 'en' | 'ar';
    language: 'en' | 'ar';
    fallbackApplied: boolean;
    availableLanguages: Array<'en' | 'ar'>;
    canonicalPath: string;
    alternatePaths: Record<string, string>;
    updatedAtUtc: string;
    publishedAtUtc?: string | null;
}

export interface CmsPageSeo {
    metaTitle?: string | null;
    metaDescription?: string | null;
    metaKeywords?: string | null;
    openGraphImageUrl?: string | null;
}

export interface CmsPageBlock {
    type: string;
    data: Record<string, any>;
}

export interface CmsPageListItem {
    slug: string;
    title: string;
    requestedLanguage: 'en' | 'ar';
    language: 'en' | 'ar';
    fallbackApplied: boolean;
    availableLanguages: Array<'en' | 'ar'>;
    updatedAtUtc: string;
    publishedAtUtc?: string | null;
}

export interface Navigation {
    header: NavigationMenu;
    footer: NavigationMenu;
    updatedAtUtc: string;
}

export interface NavigationMenu {
    links: NavigationLink[];
}

export interface NavigationLink {
    label: string;
    labelAr?: string;
    type: 'internal' | 'category' | 'cms' | 'external';
    path?: string;
    slug?: string;
    openInNewTab: boolean;
}

export function resolveNavigationLinkUrl(link: NavigationLink): string {
    switch (link.type) {
        case 'internal':
            return link.path || '/';
        case 'category':
            return `/products?category=${link.slug}`;
        case 'cms':
            return `/${link.slug}`;
        case 'external':
            return link.path || '#';
        default:
            return '#';
    }
}

export type StorefrontFooterLinkType = 'Internal' | 'External' | 'Cms';

export interface StorefrontFooter {
    footerSource: 'Tenant' | 'Global' | 'Derived';
    socialSource: 'Tenant' | 'Global' | 'None';
    sections: StorefrontFooterSection[];
    socialLinks: StorefrontSocialLink[];
    generatedAtUtc: string;
}

export interface StorefrontFooterSection {
    title: string;
    sortOrder: number;
    links: StorefrontFooterLink[];
}

export interface StorefrontFooterLink {
    label: string;
    labelAr?: string;
    type: StorefrontFooterLinkType;
    url: string;
    openInNewTab: boolean;
}

export interface StorefrontSocialLink {
    platform: string;
    url: string;
    icon?: string | null;
}

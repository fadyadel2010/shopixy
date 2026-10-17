export interface LocalizedFooterText { en: string; ar: string }
export interface FooterLink { _key: string; label: string; labelLocalized?: LocalizedFooterText; type: string; url: string; cmsPageSlug: string; openInNewTab: boolean; isActive: boolean }
export interface FooterSection { _key: string; title: string; titleLocalized?: LocalizedFooterText; isActive: boolean; links: FooterLink[] }
export interface FooterSocialLink { _key: string; platform: string; url: string; icon: string; isActive: boolean }
export interface FooterDraft { sections: FooterSection[]; socialLinks: FooterSocialLink[] }
export interface FooterRequest { expectedRevision: string; sections: { title: string; titleLocalized?: LocalizedFooterText | null; order: number; isActive: boolean; links: { label: string; labelLocalized?: LocalizedFooterText | null; type: string; url: string | null; cmsPageSlug: string | null; openInNewTab: boolean; order: number; isActive: boolean }[] }[]; socialLinks: { platform: string; url: string; icon: string | null; order: number; isActive: boolean }[] }
export function createFooterLink(isLocalized?: boolean): FooterLink;
export function createFooterSection(isLocalized?: boolean): FooterSection;
export function createSocialLink(): FooterSocialLink;
export function admitFooterConfiguration(configuration: unknown, expectedScope: string): { sections: (FooterSection & { titleLocalized: LocalizedFooterText; links: (FooterLink & { labelLocalized: LocalizedFooterText })[] })[]; socialLinks: FooterSocialLink[]; scope: string; revision: string; updatedAtUtc?: string | null };
export function mapFooterDraftToRequest(draft: FooterDraft, expectedRevision: string, isLocalized?: boolean): FooterRequest;
export function getFooterDraftFingerprint(draft: FooterDraft, isLocalized?: boolean): string;
export function validateFooterDraft(draft: FooterDraft, options?: { isLocalized?: boolean; cmsPages?: { slug?: string; title?: string }[]; allowCmsSlugEntry?: boolean }): { code: string; message: string; field: string }[];
export { getAuthoringErrorFeedback } from '../../modules/storefront/authoringPlatform';

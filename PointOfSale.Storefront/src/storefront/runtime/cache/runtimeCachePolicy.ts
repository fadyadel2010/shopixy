import { getAffiliateRefCode } from '@/bootstrap/affiliateRefTracker';
import { getTenantId, isTenantResolved } from '@/bootstrap/tenantResolver';
import { useLanguageStore } from '@/i18n/languageStore';

export const BROWSER_RUNTIME_CACHE_MAX_AGE_MS = 5 * 60 * 1000;

export interface RuntimeCacheLoadOptions {
    force?: boolean;
}

export interface RuntimeCacheIdentityOptions {
    includeAffiliate?: boolean;
    includeLanguage?: boolean;
    includeThemePreview?: boolean;
}

function getThemePreviewIdentity(): string {
    if (typeof window === 'undefined') {
        return 'none';
    }

    const token = new URLSearchParams(window.location.search).get('themePreviewToken');
    return token?.trim() ? 'signed' : 'none';
}

export function getRuntimeCacheIdentity(
    scope: string,
    options: RuntimeCacheIdentityOptions = {}
): string {
    const parts = [
        scope,
        `tenant=${isTenantResolved() ? getTenantId().toLowerCase() : 'unresolved'}`,
    ];

    if (options.includeLanguage) {
        parts.push(`language=${useLanguageStore.getState().language}`);
    }

    if (options.includeAffiliate) {
        const affiliateRef = getAffiliateRefCode()?.trim().toLowerCase();
        parts.push(`affiliate=${affiliateRef || 'none'}`);
    }

    if (options.includeThemePreview) {
        parts.push(`theme-preview=${getThemePreviewIdentity()}`);
    }

    return parts.join('|');
}

export function isRuntimeCacheFresh(
    loadedAt: number | null,
    loadedIdentity: string | null,
    currentIdentity: string,
    now = Date.now()
): boolean {
    return loadedAt !== null
        && loadedIdentity === currentIdentity
        && now - loadedAt < BROWSER_RUNTIME_CACHE_MAX_AGE_MS;
}

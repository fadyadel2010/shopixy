import { describe, expect, it } from 'vitest';
import themeAuthority from '@theme-authority/shopixy-theme-authority.json';
import {
    getAvailableMarketplaceThemes,
    getMarketplaceThemeById,
    getDefaultMarketplaceTheme,
} from './themeMarketplaceRegistry';

const PEARL_ID = 'pearl';

describe('Pearl theme — Marketplace registry (Phase T1.0 scaffold)', () => {
    it('lists Pearl in the available marketplace themes', () => {
        const ids = getAvailableMarketplaceThemes().map((t) => t.id);
        expect(ids).toContain(PEARL_ID);
    });

    it('exposes a complete Pearl manifest for the marketplace UI', () => {
        const pearl = getMarketplaceThemeById(PEARL_ID);
        expect(pearl).toBeDefined();
        expect(pearl!.name).toBe('Pearl');
        expect(pearl!.version).toBe('1.0.0');
        expect(pearl!.description).toBeTruthy();
        expect(pearl!.accent).toBeTruthy();
        expect(pearl!.capabilities).toContain('layout.header');
        expect(pearl!.capabilities).toContain('section.product-grid');
    });

    it('does not alter the default theme or duplicate any id', () => {
        const themes = getAvailableMarketplaceThemes();
        const ids = themes.map((t) => t.id.toLowerCase());
        expect(new Set(ids).size).toBe(ids.length);
        expect(getDefaultMarketplaceTheme().id).toBe('modern');
    });

    it('derives marketplace visibility from the canonical theme authority', () => {
        const canonicalMarketplaceIds = themeAuthority.themes
            .filter((theme) => theme.officialStatus === 'official' && theme.marketplaceVisibility === 'visible')
            .map((theme) => theme.id);

        expect(getAvailableMarketplaceThemes().map((theme) => theme.id)).toEqual(canonicalMarketplaceIds);
        expect(getAvailableMarketplaceThemes().map((theme) => theme.id)).not.toContain('electronics');
    });
});

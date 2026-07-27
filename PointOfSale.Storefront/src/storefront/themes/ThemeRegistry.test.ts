import { describe, expect, it } from 'vitest';
import { getActiveThemeId, resolveActiveTheme } from './ThemeResolver';
import { getAvailableThemes, getDefaultTheme } from './ThemeRegistry';

describe('storefront theme runtime registry', () => {
    it('exposes a validated manifest for every runtime theme', () => {
        const themes = getAvailableThemes();
        const ids = themes.map((theme) => theme.id.toLowerCase());

        expect(themes.length).toBeGreaterThan(0);
        expect(new Set(ids).size).toBe(ids.length);
        expect(themes.filter((theme) => theme.isDefault)).toHaveLength(1);

        for (const theme of themes) {
            expect(theme.id.trim()).toBe(theme.id);
            expect(theme.name.trim()).not.toBe('');
            expect(theme.version.trim()).not.toBe('');
            expect(theme.capabilities).toContain('layout.header');
            expect(theme.capabilities).toContain('section.hero-banner');
        }
    });

    it('falls back to the default theme for unknown persisted values', () => {
        expect(getActiveThemeId('does-not-exist')).toBe(getDefaultTheme().id);
        expect(resolveActiveTheme(null).id).toBe(getDefaultTheme().id);
    });

    it('resolves every registered theme id without replacing the active selection', () => {
        for (const theme of getAvailableThemes()) {
            expect(getActiveThemeId(theme.id)).toBe(theme.id);
        }
    });
});

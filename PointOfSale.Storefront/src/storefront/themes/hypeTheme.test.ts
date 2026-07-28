import { describe, expect, it } from 'vitest';
import { getAvailableThemes, getDefaultTheme } from './ThemeRegistry';
import { getActiveThemeId, resolveActiveTheme } from './ThemeResolver';
import { hypeThemeConfig } from './hype/themeConfig';

const HYPE_ID = 'hype';

// Mirror the exact glob pattern ThemeSectionResolver uses to discover themed
// section components. Importing the resolver itself pulls in the analytics DOM
// chain (not available in the node test env), so we assert the glob contract
// directly — this proves Hype's section files are discoverable by the runtime.
const themedSectionModules = import.meta.glob<{ default?: unknown }>('./*/sections/*.tsx');
const hypeSectionPaths = Object.keys(themedSectionModules).filter((p) => p.startsWith('./hype/sections/'));

describe('Hype theme — Phase H1.0 scaffold registration', () => {
    it('is registered in the runtime theme registry', () => {
        const ids = getAvailableThemes().map((t) => t.id);
        expect(ids).toContain(HYPE_ID);
    });

    it('carries a valid manifest (name, version, capabilities)', () => {
        const hype = getAvailableThemes().find((t) => t.id === HYPE_ID);
        expect(hype).toBeDefined();
        expect(hype!.name.trim()).toBe('Hype Electronics');
        expect(hype!.version.trim()).toBe('1.0.0');
        expect(hype!.capabilities).toContain('layout.header');
        expect(hype!.capabilities).toContain('section.hero-banner');
        expect(hype!.capabilities).toContain('section.product-grid');
        expect(hype!.capabilities).toContain('section.category-grid');
    });

    it('does not introduce a duplicate id (registry uniqueness intact)', () => {
        const ids = getAvailableThemes().map((t) => t.id.toLowerCase());
        expect(new Set(ids).size).toBe(ids.length);
        expect(getAvailableThemes().filter((t) => t.isDefault)).toHaveLength(1);
    });

    it('resolves through ThemeResolver', () => {
        expect(resolveActiveTheme(HYPE_ID).id).toBe(HYPE_ID);
        expect(getActiveThemeId(HYPE_ID)).toBe(HYPE_ID);
    });

    it('ThemeSectionResolver discovers Hype section modules (auto-glob contract)', () => {
        expect(hypeSectionPaths).toContain('./hype/sections/hero-banner.tsx');
        expect(hypeSectionPaths).toContain('./hype/sections/product-grid.tsx');
        expect(hypeSectionPaths).toContain('./hype/sections/category-grid.tsx');
        // The glob must actually resolve the modules (eagerly evaluated here).
        for (const path of hypeSectionPaths) {
            expect(typeof themedSectionModules[path]).toBe('function');
        }
    });

    it('runtime fallback to default is unchanged for unknown ids', () => {
        expect(getActiveThemeId('does-not-exist')).toBe(getDefaultTheme().id);
        expect(resolveActiveTheme(null).id).toBe(getDefaultTheme().id);
    });

    it('initial ThemeConfig tokens are well-formed (orange/cyan/dark system)', () => {
        // Brand / nav / CTA accent (--hd-text #F06736)
        expect(hypeThemeConfig.colors.primary).toBe('#F06736');
        // Section heading ink (#212A2F)
        expect(hypeThemeConfig.colors.secondary).toBe('#212A2F');
        // Bright white canvas
        expect(hypeThemeConfig.colors.background).toBe('#FFFFFF');
        // Body / price text
        expect(hypeThemeConfig.colors.text).toBe('#131316');
        expect(hypeThemeConfig.font.family).toContain('Jost');
        expect(hypeThemeConfig.font.baseSize).toBe('16px');
        // Product cards use 8px radius (category/header/buttons override in components)
        expect(hypeThemeConfig.ui.borderRadius).toBe('8px');
        expect(['compact', 'default', 'detailed']).toContain(hypeThemeConfig.layout.productCardVariant);
        expect(['standard', 'centered', 'minimal']).toContain(hypeThemeConfig.layout.headerVariant);
    });
});

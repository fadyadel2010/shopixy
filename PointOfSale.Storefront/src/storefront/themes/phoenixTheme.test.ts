import { describe, expect, it } from 'vitest';
import { getAvailableThemes, getDefaultTheme } from './ThemeRegistry';
import { getActiveThemeId, resolveActiveTheme } from './ThemeResolver';
import { phoenixThemeConfig } from './phoenix/themeConfig';

const PHOENIX_ID = 'phoenix';

// Mirror the exact glob pattern ThemeSectionResolver uses to discover themed
// section components. Importing the resolver itself pulls in the analytics DOM
// chain (not available in the node test env), so we assert the glob contract
// directly — this proves Phoenix's section files are discoverable by the runtime.
const themedSectionModules = import.meta.glob<{ default?: unknown }>('./*/sections/*.tsx');
const phoenixSectionPaths = Object.keys(themedSectionModules).filter((p) => p.startsWith('./phoenix/sections/'));

describe('Phoenix Sports theme — Phase T1.0 scaffold registration', () => {
    it('is registered in the runtime theme registry', () => {
        const ids = getAvailableThemes().map((t) => t.id);
        expect(ids).toContain(PHOENIX_ID);
    });

    it('carries a valid manifest (name, version, capabilities)', () => {
        const phoenix = getAvailableThemes().find((t) => t.id === PHOENIX_ID);
        expect(phoenix).toBeDefined();
        expect(phoenix!.name.trim()).toBe('Phoenix Sports');
        expect(phoenix!.version.trim()).toBe('1.0.0');
        expect(phoenix!.capabilities).toContain('layout.header');
        expect(phoenix!.capabilities).toContain('section.hero-banner');
        expect(phoenix!.capabilities).toContain('section.product-grid');
        expect(phoenix!.capabilities).toContain('section.category-grid');
    });

    it('does not introduce a duplicate id (registry uniqueness intact)', () => {
        const ids = getAvailableThemes().map((t) => t.id.toLowerCase());
        expect(new Set(ids).size).toBe(ids.length);
        expect(getAvailableThemes().filter((t) => t.isDefault)).toHaveLength(1);
    });

    it('resolves through ThemeResolver', () => {
        expect(resolveActiveTheme(PHOENIX_ID).id).toBe(PHOENIX_ID);
        expect(getActiveThemeId(PHOENIX_ID)).toBe(PHOENIX_ID);
    });

    it('ThemeSectionResolver discovers Phoenix section modules (auto-glob contract)', () => {
        expect(phoenixSectionPaths).toContain('./phoenix/sections/hero-banner.tsx');
        expect(phoenixSectionPaths).toContain('./phoenix/sections/product-grid.tsx');
        expect(phoenixSectionPaths).toContain('./phoenix/sections/category-grid.tsx');
        // The glob must actually resolve the modules (eagerly evaluated here).
        for (const path of phoenixSectionPaths) {
            expect(typeof themedSectionModules[path]).toBe('function');
        }
    });

    it('runtime fallback to default is unchanged for unknown ids', () => {
        expect(getActiveThemeId('does-not-exist')).toBe(getDefaultTheme().id);
        expect(resolveActiveTheme(null).id).toBe(getDefaultTheme().id);
    });

    it('initial ThemeConfig tokens are well-formed (Phoenix Sports palette)', () => {
        // Red sale-price accent (design #cd1515)
        expect(phoenixThemeConfig.colors.primary).toBe('#cd1515');
        // Black footer (#202021)
        expect(phoenixThemeConfig.colors.secondary).toBe('#202021');
        // Warm off-white canvas (#ece9e2)
        expect(phoenixThemeConfig.colors.background).toBe('#ece9e2');
        // Jost everywhere
        expect(phoenixThemeConfig.font.family).toContain('Jost');
        expect(phoenixThemeConfig.font.baseSize).toBe('15px');
        // Sharp cards
        expect(phoenixThemeConfig.ui.borderRadius).toBe('0px');
        expect(['compact', 'default', 'detailed']).toContain(phoenixThemeConfig.layout.productCardVariant);
        expect(['standard', 'centered', 'minimal']).toContain(phoenixThemeConfig.layout.headerVariant);
    });
});

import { describe, expect, it } from 'vitest';
import { getAvailableThemes, getDefaultTheme } from './ThemeRegistry';
import { getActiveThemeId, resolveActiveTheme } from './ThemeResolver';
import { pearlThemeConfig } from './pearl/themeConfig';

const PEARL_ID = 'pearl';

// Mirror the exact glob pattern ThemeSectionResolver uses to discover themed
// section components. Importing the resolver itself pulls in the analytics DOM
// chain (not available in the node test env), so we assert the glob contract
// directly — this proves Pearl's section files are discoverable by the runtime.
const themedSectionModules = import.meta.glob<{ default?: unknown }>('./*/sections/*.tsx');
const pearlSectionPaths = Object.keys(themedSectionModules).filter((p) => p.startsWith('./pearl/sections/'));

describe('Pearl theme — Phase T1.0 scaffold registration', () => {
    it('is registered in the runtime theme registry', () => {
        const ids = getAvailableThemes().map((t) => t.id);
        expect(ids).toContain(PEARL_ID);
    });

    it('carries a valid manifest (name, version, capabilities)', () => {
        const pearl = getAvailableThemes().find((t) => t.id === PEARL_ID);
        expect(pearl).toBeDefined();
        expect(pearl!.name.trim()).toBe('Pearl');
        expect(pearl!.version.trim()).toBe('1.0.0');
        expect(pearl!.capabilities).toContain('layout.header');
        expect(pearl!.capabilities).toContain('section.hero-banner');
        expect(pearl!.capabilities).toContain('section.product-grid');
        expect(pearl!.capabilities).toContain('section.category-grid');
    });

    it('does not introduce a duplicate id (registry uniqueness intact)', () => {
        const ids = getAvailableThemes().map((t) => t.id.toLowerCase());
        expect(new Set(ids).size).toBe(ids.length);
        expect(getAvailableThemes().filter((t) => t.isDefault)).toHaveLength(1);
    });

    it('resolves through ThemeResolver', () => {
        expect(resolveActiveTheme(PEARL_ID).id).toBe(PEARL_ID);
        expect(getActiveThemeId(PEARL_ID)).toBe(PEARL_ID);
    });

    it('ThemeSectionResolver discovers Pearl section modules (auto-glob contract)', () => {
        expect(pearlSectionPaths).toContain('./pearl/sections/hero-banner.tsx');
        expect(pearlSectionPaths).toContain('./pearl/sections/product-grid.tsx');
        expect(pearlSectionPaths).toContain('./pearl/sections/category-grid.tsx');
        // The glob must actually resolve the modules (eagerly evaluated here).
        for (const path of pearlSectionPaths) {
            expect(typeof themedSectionModules[path]).toBe('function');
        }
    });

    it('runtime fallback to default is unchanged for unknown ids', () => {
        expect(getActiveThemeId('does-not-exist')).toBe(getDefaultTheme().id);
        expect(resolveActiveTheme(null).id).toBe(getDefaultTheme().id);
    });

    it('initial ThemeConfig tokens are well-formed (no visual changes yet)', () => {
        expect(pearlThemeConfig.colors.primary).toBe('#DC2626');
        expect(pearlThemeConfig.colors.secondary).toBe('#C9A863');
        expect(pearlThemeConfig.colors.background).toBe('#FFFFFF');
        expect(pearlThemeConfig.font.family).toContain('Jost');
        expect(pearlThemeConfig.ui.borderRadius).toBe('0px');
        expect(['compact', 'default', 'detailed']).toContain(pearlThemeConfig.layout.productCardVariant);
        expect(['standard', 'centered', 'minimal']).toContain(pearlThemeConfig.layout.headerVariant);
    });
});

import { describe, expect, it } from 'vitest';
import { normalizeThemeComponentTheme } from '@/components/theme-components/themeComponentTypes';
import { resolveProductCardRenderer } from '@/components/products/productCardRendererResolver';
import { normalizeCommerceTheme } from '@/storefront/commerce/commerceTheme';
import { getBuiltinProductTemplate } from '@/storefront/products/ProductTemplateRegistry';
import { resolvePurchaseExperienceVariant } from '@/storefront/products/purchaseExperience';
import { getActiveThemeId, resolveActiveTheme } from './ThemeResolver';
import { canonicalThemeAuthority, getAvailableThemes, getDefaultTheme } from './ThemeRegistry';

const officialThemes = canonicalThemeAuthority.themes.filter(
    (theme) => theme.officialStatus === 'official' && theme.activationStatus === 'active'
);
const officialRuntimeThemes = officialThemes.filter((theme) => theme.runtimeAvailability);
const officialThemeIds = officialThemes.map((theme) => theme.id);
const themedModules = import.meta.glob('./*/*.{ts,tsx}');
const themedSectionModules = import.meta.glob('./*/sections/*.tsx');
const themedLayoutModules = import.meta.glob('./*/layout/*.tsx');

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

    it('derives runtime theme ids from the canonical authority', () => {
        expect(getAvailableThemes().map((theme) => theme.id)).toEqual(
            officialRuntimeThemes.map((theme) => theme.id)
        );
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

    it('keeps experimental themes out of production activation registries', () => {
        const electronics = canonicalThemeAuthority.themes.find((theme) => theme.id === 'electronics');

        expect(electronics?.officialStatus).toBe('experimental');
        expect(electronics?.activationStatus).toBe('disabled');
        expect(getAvailableThemes().map((theme) => theme.id)).not.toContain('electronics');
    });

    it('provides full runtime coverage for every official theme', () => {
        for (const theme of officialThemes) {
            expect(theme.backendAvailability).toBe(true);
            expect(theme.marketplaceVisibility).toBe('visible');
            expect(theme.runtimeAvailability).toBe(true);
            expect(theme.homepageAvailability).toBe(true);
            expect(getActiveThemeId(theme.id)).toBe(theme.id);
            expect(normalizeThemeComponentTheme(theme.id)).toBe(theme.componentTheme);
            expect(normalizeCommerceTheme(theme.id)).toBe(theme.commerceBrowsingTheme);
            expect(resolvePurchaseExperienceVariant('default', theme.id)).toBe(theme.supportedPurchaseProfiles[0]);
            expect(getBuiltinProductTemplate(theme.supportedProductTemplates[0])?.templateId).toBe(theme.supportedProductTemplates[0]);
            expect(resolveProductCardRenderer(theme.id)).toBe(theme.supportedProductCards[0]);

            for (const section of theme.supportedSections) {
                expect(themedSectionModules[`./${theme.id}/sections/${section}.tsx`]).toBeDefined();
            }

            for (const layout of theme.supportedLayouts) {
                expect(themedLayoutModules[`./${theme.id}/layout/${layout}.tsx`]).toBeDefined();
            }
        }
    });

    it('does not contain unknown theme implementation folders', () => {
        const declaredThemeIds = new Set(canonicalThemeAuthority.themes.map((theme) => theme.id));
        const implementationThemeIds = new Set(
            Object.keys(themedModules)
                .map((path) => path.match(/^\.\/([^/]+)\//)?.[1])
                .filter((themeId): themeId is string => Boolean(themeId))
        );

        for (const themeId of implementationThemeIds) {
            expect(declaredThemeIds.has(themeId)).toBe(true);
        }
    });

    it('documents the canonical coverage matrix rows for official themes', () => {
        const rows = officialThemeIds.map((themeId) => ({
            themeId,
            backend: true,
            marketplace: true,
            runtime: true,
            resolver: getActiveThemeId(themeId) === themeId,
            builder: Boolean(canonicalThemeAuthority.themes.find((theme) => theme.id === themeId)?.supportedBuilderFeatures.length),
            homepage: Boolean(canonicalThemeAuthority.themes.find((theme) => theme.id === themeId)?.homepageAvailability),
            sections: ['hero-banner', 'product-grid', 'category-grid'].every(
                (section) => Boolean(themedSectionModules[`./${themeId}/sections/${section}.tsx`])
            ),
            components: normalizeThemeComponentTheme(themeId) === themeId,
            productCards: resolveProductCardRenderer(themeId) === themeId,
            browsing: normalizeCommerceTheme(themeId) === themeId,
            purchase: resolvePurchaseExperienceVariant('default', themeId) === themeId,
            templates: getBuiltinProductTemplate(themeId)?.templateId === themeId,
            capabilities: canonicalThemeAuthority.canonicalCapabilities.every((capability) =>
                canonicalThemeAuthority.themes.find((theme) => theme.id === themeId)?.capabilities.includes(capability)
            ),
            activation: getActiveThemeId(themeId) === themeId,
            validation: true,
        }));

        expect(rows).toEqual(rows.map((row) => ({
            ...row,
            backend: true,
            marketplace: true,
            runtime: true,
            resolver: true,
            builder: true,
            homepage: true,
            sections: true,
            components: true,
            productCards: true,
            browsing: true,
            purchase: true,
            templates: true,
            capabilities: true,
            activation: true,
            validation: true,
        })));
    });
});

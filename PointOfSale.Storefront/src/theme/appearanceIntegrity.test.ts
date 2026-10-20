import { describe, expect, it } from 'vitest';
import authority from '@theme-authority/shopixy-theme-authority.json';
import { appearanceRoles, contrastRatio, appearanceContrastWarnings } from '@theme-authority/appearanceRoles';
import { themeToCssVariables } from './cssVariables';
import { DEFAULT_STORE_CONFIG } from '@/types/storeConfig';
import { getPurchaseExperienceProfile } from '@/storefront/products/purchaseExperience';
import { readFileSync } from 'node:fs';

const dark = { primary:'#3b82f6', secondary:'#1e293b', background:'#0f172a', text:'#f8fafc', muted:'#94a3b8' };
describe('appearance roles across all supported themes and color presets', () => {
    for (const theme of authority.themes.filter(t => t.runtimeAvailability)) {
        it(`${theme.id} keeps palette values authoritative and derives readable foregrounds`, () => {
            const colors = theme.defaultThemeConfig.colors;
            const variables = themeToCssVariables(theme.defaultThemeConfig as typeof DEFAULT_STORE_CONFIG.theme);
            const roles = appearanceRoles(colors);
            for (const [key,value] of Object.entries(colors)) expect(variables[`--color-${key}`]).toBe(value);
            expect(contrastRatio(roles['on-primary'],colors.primary)).toBeGreaterThanOrEqual(4.5);
            expect(contrastRatio(roles['on-secondary'],colors.secondary)).toBeGreaterThanOrEqual(4.5);
            expect(contrastRatio(roles['primary-text'],colors.background)).toBeGreaterThanOrEqual(4.5);
            expect(contrastRatio(roles['secondary-text'],colors.background)).toBeGreaterThanOrEqual(4.5);
            expect(appearanceContrastWarnings(colors)).toEqual([]);
            const profile = getPurchaseExperienceProfile('default',theme.id);
            expect(profile.buyBoxClassName).toContain('bg-background');
            expect(profile.titleClassName).toContain('text-text');
            expect(profile.ctaClassName).toContain('text-on-primary');
            expect(profile.categoryChipClassName).toContain('bg-background');
            for (const section of ['category-grid','product-grid']) {
                const source=readFileSync(new URL(`../storefront/themes/${theme.id}/sections/${section}.tsx`,import.meta.url),'utf8');
                expect(source).toContain('bg-background');
                expect(source).not.toMatch(/<section[^>]*(?:bg-white|bg-slate-|bg-\[)/);
            }
        });
    }
    it('primary-only editing never overwrites other stored roles or their channels', () => {
        const original=themeToCssVariables({...DEFAULT_STORE_CONFIG.theme, colors:dark});
        const changed=themeToCssVariables({...DEFAULT_STORE_CONFIG.theme, colors:{...dark,primary:'#f0c040'}});
        for (const key of ['secondary','background','text','muted']) {
            expect(changed[`--color-${key}`]).toBe(original[`--color-${key}`]);
            expect(changed[`--color-${key}-rgb`]).toBe(original[`--color-${key}-rgb`]);
        }
        expect(appearanceContrastWarnings(dark)).toEqual([]);
    });
    it('incomplete legacy values get established fallbacks and no stale token keys', () => {
        const incomplete=themeToCssVariables({colors:{primary:'#123456'}} as typeof DEFAULT_STORE_CONFIG.theme);
        const complete=themeToCssVariables(DEFAULT_STORE_CONFIG.theme);
        expect(Object.keys(incomplete)).toEqual(Object.keys(complete));
        expect(incomplete['--color-background']).toBe('#ffffff');
        expect(incomplete['--color-text']).toBe('#020617');
    });
    it('warns rather than modifying unreadable merchant text', () => {
        const colors={...dark,text:dark.background};
        expect(appearanceContrastWarnings(colors)[0]).toContain('text: 1.00:1');
        expect(themeToCssVariables({...DEFAULT_STORE_CONFIG.theme, colors})['--color-text']).toBe(dark.background);
    });
    it('does not put dark default ink on an incomplete legacy dark canvas', () => {
        const variables=themeToCssVariables({colors:{background:dark.background}} as typeof DEFAULT_STORE_CONFIG.theme);
        expect(contrastRatio(variables['--color-text'],dark.background)).toBeGreaterThanOrEqual(4.5);
        expect(contrastRatio(variables['--color-muted'],dark.background)).toBeGreaterThanOrEqual(4.5);
    });
});

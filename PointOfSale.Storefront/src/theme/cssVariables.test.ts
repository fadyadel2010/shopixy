import { describe, expect, it } from 'vitest';
import type { ThemeConfig } from '@/types';
import { CSS_VARIABLES, themeToCssVariables } from './cssVariables';

describe('theme CSS variable projection', () => {
    it('updates every existing CSS variable when runtime ThemeConfig changes', () => {
        const pearl = themeToCssVariables(themeConfig({
            primary: '#DC2626',
            secondary: '#C9A863',
            background: '#FBFAF7',
            text: '#131316',
            muted: '#313E61',
            family: "'Jost', sans-serif",
            baseSize: '15px',
            borderRadius: '0px',
        }));

        const fashion = themeToCssVariables(themeConfig({
            primary: '#e11d48',
            secondary: '#7c2d12',
            background: '#fff7ed',
            text: '#1f2937',
            muted: '#9f1239',
            family: 'Poppins',
            baseSize: '17px',
            borderRadius: '16px',
        }));

        expect(Object.values(CSS_VARIABLES).sort()).toEqual(Object.keys(pearl).sort());
        expect(Object.values(CSS_VARIABLES).sort()).toEqual(Object.keys(fashion).sort());

        for (const variable of Object.values(CSS_VARIABLES)) {
            expect(fashion[variable]).not.toBe(pearl[variable]);
        }
    });

    it('derives RGB channels and the muted alias used by catalog hover states', () => {
        const variables = themeToCssVariables(themeConfig({
            primary: '#AbC',
            secondary: '#102030',
            background: '#ffffff',
            text: '#111111',
            muted: '#778899',
            family: 'Cairo',
            baseSize: '18px',
            borderRadius: '12px',
        }));

        expect(variables[CSS_VARIABLES.COLOR_PRIMARY_RGB]).toBe('170, 187, 204');
        expect(variables[CSS_VARIABLES.COLOR_SECONDARY_RGB]).toBe('16, 32, 48');
        expect(variables[CSS_VARIABLES.COLOR_TEXT_MUTED]).toBe('#778899');
    });
});

function themeConfig(tokens: {
    primary: string;
    secondary: string;
    background: string;
    text: string;
    muted: string;
    family: string;
    baseSize: string;
    borderRadius: string;
}): ThemeConfig {
    return {
        colors: {
            primary: tokens.primary,
            secondary: tokens.secondary,
            background: tokens.background,
            text: tokens.text,
            muted: tokens.muted,
        },
        font: {
            family: tokens.family,
            baseSize: tokens.baseSize,
        },
        ui: {
            borderRadius: tokens.borderRadius,
            buttonStyle: 'solid',
        },
        layout: {
            productCardVariant: 'compact',
            headerVariant: 'centered',
        },
    };
}

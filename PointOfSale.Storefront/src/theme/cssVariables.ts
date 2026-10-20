/**
 * CSS Variables Configuration
 * 
 * Maps StoreConfig theme values to CSS custom properties.
 * Applied to :root on app bootstrap.
 */

import type { ThemeConfig } from '@/types';
import { DEFAULT_STORE_CONFIG } from '@/types/storeConfig';
import { appearanceRoles, contrastRatio, isHexColor, onColor, rgbChannels } from '@theme-authority/appearanceRoles';

/**
 * CSS Variable names mapped from theme config
 */
export const CSS_VARIABLES = {
    // Colors
    COLOR_PRIMARY: '--color-primary',
    COLOR_SECONDARY: '--color-secondary',
    COLOR_BACKGROUND: '--color-background',
    COLOR_TEXT: '--color-text',
    COLOR_MUTED: '--color-muted',
    COLOR_PRIMARY_RGB: '--color-primary-rgb',
    COLOR_SECONDARY_RGB: '--color-secondary-rgb',
    COLOR_TEXT_MUTED: '--color-text-muted',

    // Typography
    FONT_FAMILY: '--font-family',
    FONT_FAMILY_AR: '--font-family-ar',
    FONT_BASE_SIZE: '--font-base-size',

    // UI
    BORDER_RADIUS: '--border-radius',
} as const;

/**
 * Generate CSS variable object from theme config
 */
export const themeToCssVariables = (theme: ThemeConfig): Record<string, string> => {
    const fallback = DEFAULT_STORE_CONFIG.theme;
    const colors = Object.fromEntries(Object.entries(fallback.colors).map(([key, value]) => [
        key, isHexColor(theme.colors?.[key as keyof typeof fallback.colors])
            ? theme.colors[key as keyof typeof fallback.colors] : value,
    ])) as typeof fallback.colors;
    // A legacy dark canvas with omitted text must not inherit dark framework ink.
    // Valid merchant selections remain untouched (the editor warns about contrast).
    if (!isHexColor(theme.colors?.text) && contrastRatio(colors.text, colors.background) < 4.5) {
        colors.text = onColor(colors.background);
    }
    if (!isHexColor(theme.colors?.muted) && contrastRatio(colors.muted, colors.background) < 4.5) {
        colors.muted = colors.text;
    }
    const roles = appearanceRoles(colors);
    const paletteVariables = Object.fromEntries(Object.entries({ ...colors, ...roles }).flatMap(([key, value]) => [
        [`--color-${key}`, value], [`--color-${key}-rgb`, rgbChannels(value).join(', ')],
    ]));
    // Known Arabic-compatible fonts from POS Theme Editor
    const arabicCompatibleFonts = ['Cairo', 'Tajawal', 'Noto Sans Arabic', 'Amiri', 'Noto Naskh Arabic'];

    // Check if selected font is Arabic-compatible
    const selectedFont = theme.font?.family || fallback.font.family;
    const isArabicFont = arabicCompatibleFonts.some(f =>
        selectedFont.toLowerCase().includes(f.toLowerCase())
    );

    // Default Arabic font stack
    const defaultArabicFonts = "'Cairo', 'Tajawal', 'Noto Sans Arabic'";

    // Build Arabic font family:
    // - If user explicitly set familyAr, use that
    // - If selected font is Arabic-compatible, use it first
    // - Otherwise use default Arabic fonts + selected font as fallback
    let arabicFontFamily: string;
    if (theme.font?.familyAr) {
        arabicFontFamily = theme.font.familyAr;
    } else if (isArabicFont) {
        // User selected an Arabic font, use it as primary for Arabic text
        arabicFontFamily = `'${selectedFont}', ${defaultArabicFonts}`;
    } else {
        // User selected a non-Arabic font, keep Arabic fonts first
        arabicFontFamily = defaultArabicFonts;
    }

    return {
        ...paletteVariables,
        [CSS_VARIABLES.COLOR_TEXT_MUTED]: colors.muted,
        [CSS_VARIABLES.FONT_FAMILY]: selectedFont,
        [CSS_VARIABLES.FONT_FAMILY_AR]: `${arabicFontFamily}, ${selectedFont}`,
        [CSS_VARIABLES.FONT_BASE_SIZE]: theme.font?.baseSize || fallback.font.baseSize,
        [CSS_VARIABLES.BORDER_RADIUS]: theme.ui?.borderRadius || fallback.ui.borderRadius,
    };
};

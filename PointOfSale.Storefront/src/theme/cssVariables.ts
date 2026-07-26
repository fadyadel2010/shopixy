/**
 * CSS Variables Configuration
 * 
 * Maps StoreConfig theme values to CSS custom properties.
 * Applied to :root on app bootstrap.
 */

import type { ThemeConfig } from '@/types';

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
    // Known Arabic-compatible fonts from POS Theme Editor
    const arabicCompatibleFonts = ['Cairo', 'Tajawal', 'Noto Sans Arabic', 'Amiri', 'Noto Naskh Arabic'];

    // Check if selected font is Arabic-compatible
    const selectedFont = theme.font.family;
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
    if (theme.font.familyAr) {
        arabicFontFamily = theme.font.familyAr;
    } else if (isArabicFont) {
        // User selected an Arabic font, use it as primary for Arabic text
        arabicFontFamily = `'${selectedFont}', ${defaultArabicFonts}`;
    } else {
        // User selected a non-Arabic font, keep Arabic fonts first
        arabicFontFamily = defaultArabicFonts;
    }

    return {
        [CSS_VARIABLES.COLOR_PRIMARY]: theme.colors.primary,
        [CSS_VARIABLES.COLOR_SECONDARY]: theme.colors.secondary,
        [CSS_VARIABLES.COLOR_BACKGROUND]: theme.colors.background,
        [CSS_VARIABLES.COLOR_TEXT]: theme.colors.text,
        [CSS_VARIABLES.COLOR_MUTED]: theme.colors.muted,
        [CSS_VARIABLES.FONT_FAMILY]: theme.font.family,
        [CSS_VARIABLES.FONT_FAMILY_AR]: `${arabicFontFamily}, ${theme.font.family}`,
        [CSS_VARIABLES.FONT_BASE_SIZE]: theme.font.baseSize,
        [CSS_VARIABLES.BORDER_RADIUS]: theme.ui.borderRadius,
    };
};

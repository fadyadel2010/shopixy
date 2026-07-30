/**
 * Sharp Jewelry Theme — Initial ThemeConfig (Phase T1.0 scaffold)
 *
 * Infrastructure-only artifact. This is the canonical *initial* presentation
 * configuration for the Sharp Jewelry theme. It is derived from the Sharp Jewelry
 * design reference (docs/sharp-jewelry-theme-design-report.md) and expresses only the
 * theme tokens: colors, typography, layout defaults, UI defaults, border radius.
 *
 * No component, section, or visual implementation is introduced here. Components
 * under `sharp/sections/*` and `sharp/layout/*` are customized to match the
 * Sharp Jewelry reference site.
 *
 * These values feed the existing Theme SDK flow:
 *   ThemeConfig -> cssVariables.themeToCssVariables -> applyTheme (CSS vars on :root)
 */

import type { ThemeConfig } from '@/types';

export const sharpThemeConfig: ThemeConfig = {
    colors: {
        // Red accent used for carousel arrows + hover states (design: #FF5A5A)
        primary: '#FF5A5A',
        // Emerald green used for header, announcement, footer, section headings (design: #013423)
        secondary: '#013423',
        // White page canvas
        background: '#FFFFFF',
        // Body / price text (design: #131316)
        text: '#131316',
        // Emerald section headings (design: #013423)
        muted: '#013423',
    },
    font: {
        // Instrument Sans is the UI/body/name/price font; Playfair Display is the serif
        // display font loaded for newsletter headings via index.html Google Fonts link.
        family: "'Instrument Sans', sans-serif",
        familyAr: "'Almarai', 'Tajawal', 'Noto Sans Arabic'",
        baseSize: '16px',
    },
    ui: {
        // Editorial, sharp corners (design: border-radius 0)
        borderRadius: '0px',
        buttonStyle: 'solid',
    },
    layout: {
        productCardVariant: 'compact',
        headerVariant: 'standard',
    },
};

export default sharpThemeConfig;

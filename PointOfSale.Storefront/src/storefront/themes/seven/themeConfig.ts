/**
 * Seven Womenwear Theme — Initial ThemeConfig
 *
 * Infrastructure-only artifact. This is the canonical *initial* presentation
 * configuration for the Seven Womenwear theme. It is derived from the
 * seven-womenwear design reference (docs/seven-womenwear-theme-design-report.md)
 * and expresses only the theme tokens: colors, typography, layout defaults,
 * UI defaults, border radius.
 *
 * These values feed the existing Theme SDK flow:
 *   ThemeConfig -> cssVariables.themeToCssVariables -> applyTheme (CSS vars on :root)
 */

import type { ThemeConfig } from '@/types';

export const sevenThemeConfig: ThemeConfig = {
    colors: {
        // Mauve-terracotta header/announcement/footer bg (design: #9D7877)
        primary: '#9D7877',
        // Warm red sale badge bg + sale price (design: #CD1515)
        secondary: '#CD1515',
        // White page canvas
        background: '#FFFFFF',
        // Body / price text (design: #131316)
        text: '#131316',
        // Product name text (design: #242424)
        muted: '#242424',
    },
    font: {
        // Jost is the UI/body/name/price font; Newsreader is the serif display
        // font loaded for hero headings via index.html Google Fonts link.
        family: "'Jost', sans-serif",
        familyAr: "'Cairo', 'Tajawal', 'Noto Sans Arabic'",
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

export default sevenThemeConfig;

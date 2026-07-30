/**
 * Alizon Theme — Initial ThemeConfig (Phase A1.0 scaffold)
 *
 * Infrastructure-only artifact. This is the canonical *initial* presentation
 * configuration for the Alizon theme. It is derived from the ALIZON storefront
 * design reference (docs/alizon-theme-design-report.md) and expresses
 * only the theme tokens: colors, typography, layout defaults, UI defaults,
 * border radius.
 *
 * No component, section, or visual implementation is introduced here. Components
 * under `alizon/sections/*` and `alizon/layout/*` are verbatim copies of the
 * Hype theme, recolored to match the reference site, and will be visually
 * customized in a later phase.
 *
 * These values feed the existing Theme SDK flow:
 *   ThemeConfig -> cssVariables.themeToCssVariables -> applyTheme (CSS vars on :root)
 *
 * Alizon owns a native shared component, product-card, browsing, and purchase
 * registration. It is not an alias of the experimental Electronics theme.
 */

import type { ThemeConfig } from '@/types';

export const alizonThemeConfig: ThemeConfig = {
    colors: {
        // Brand / nav / CTA accent (reference: #FF0000)
        primary: '#FF0000',
        // Section heading ink (reference: #121212)
        secondary: '#121212',
        // Bright white product canvas (reference: #FFFFFF)
        background: '#FFFFFF',
        // Body / price text (reference: #131316)
        text: '#131316',
        // Secondary / caption text (reference: #6D6D74)
        muted: '#6D6D74',
    },
    font: {
        // Jost is the UI/body/name/price font; Newsreader is the serif display
        // font loaded for hero headings via index.html Google Fonts link.
        family: "'Jost', sans-serif",
        familyAr: "'Cairo', 'Tajawal', 'Noto Sans Arabic'",
        baseSize: '16px',
    },
    ui: {
        // Product cards use 0px radius (sharp, editorial corners per reference site)
        borderRadius: '0px',
        buttonStyle: 'solid',
    },
    layout: {
        productCardVariant: 'compact',
        headerVariant: 'standard',
    },
};

export default alizonThemeConfig;

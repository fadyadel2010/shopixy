/**
 * Hype Theme — Initial ThemeConfig (Phase H1.0 scaffold)
 *
 * Infrastructure-only artifact. This is the canonical *initial* presentation
 * configuration for the Hype theme. It is derived from the Hype Electronics
 * design reference (docs/hype-electronics-theme-design-report.md) and expresses
 * only the theme tokens: colors, typography, layout defaults, UI defaults,
 * border radius.
 *
 * No component, section, or visual implementation is introduced here. Components
 * under `hype/sections/*` and `hype/layout/*` are verbatim copies of the
 * Pearl theme, recolored to match the reference site, and will be visually
 * customized in a later phase.
 *
 * These values feed the existing Theme SDK flow:
 *   ThemeConfig -> cssVariables.themeToCssVariables -> applyTheme (CSS vars on :root)
 *
 * Hype owns a native shared component, product-card, browsing, and purchase
 * registration. It is not an alias of the experimental Electronics theme.
 */

import type { ThemeConfig } from '@/types';

export const hypeThemeConfig: ThemeConfig = {
    colors: {
        // Brand / nav / CTA accent (reference: #F06736, --hd-text)
        primary: '#F06736',
        // Section heading ink (reference: #212A2F — de-facto brand slate)
        secondary: '#212A2F',
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
        // Product cards use 8px radius; category cards / header / buttons
        // override to 0 / pill inside the components.
        borderRadius: '8px',
        buttonStyle: 'solid',
    },
    layout: {
        productCardVariant: 'compact',
        headerVariant: 'standard',
    },
};

export default hypeThemeConfig;

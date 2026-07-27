/**
 * Pearl Theme — Initial ThemeConfig (Phase T1.0 scaffold)
 *
 * Infrastructure-only artifact. This is the canonical *initial* presentation
 * configuration for the Pearl theme. It is derived from the Pearl Watches design
 * reference (docs/pearl-watches-theme-design-report.md) and expresses only the
 * theme tokens: colors, typography, layout defaults, UI defaults, border radius.
 *
 * No component, section, or visual implementation is introduced here. Components
 * under `pearl/sections/*` and `pearl/layout/*` are verbatim copies of the
 * Fashion theme and will be visually customized in a later phase.
 *
 * These values feed the existing Theme SDK flow:
 *   ThemeConfig -> cssVariables.themeToCssVariables -> applyTheme (CSS vars on :root)
 */

import type { ThemeConfig } from '@/types';

export const pearlThemeConfig: ThemeConfig = {
    colors: {
        // Red accent used for CTA + LIMITED EDITION marquee (design: #DC2626)
        primary: '#DC2626',
        // Gold footer text (design: #C9A863)
        secondary: '#C9A863',
        // Light page canvas (design: #FFFFFF)
        background: '#FFFFFF',
        // Body / price text (design: #131316)
        text: '#131316',
        // Navy section headings (design: #313E61)
        muted: '#313E61',
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
        headerVariant: 'centered',
    },
};

export default pearlThemeConfig;

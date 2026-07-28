/**
 * Phoenix Sports Theme — Initial ThemeConfig (Phase T1.0 + visual skin)
 *
 * Presentation configuration for the Phoenix Sports theme, derived from the
 * Phoenix Sports design reference (docs/phoenix-sports-theme-design-report.md).
 * Expresses the theme tokens: colors, typography, layout defaults, UI defaults,
 * border radius.
 *
 * These values feed the existing Theme SDK flow:
 *   ThemeConfig -> cssVariables.themeToCssVariables -> applyTheme (CSS vars on :root)
 *
 * Design tokens (all measured live from the reference site):
 *   - Warm off-white canvas (#ece9e2), body text #131316, black UI (#000).
 *   - Red sale accent (#cd1515 sale price, #C94A4A badge).
 *   - Black footer (#202021) with white text.
 *   - Jost for all UI/nav/names/prices; Playfair Display for hero/section display.
 *   - Sharp cards (radius 0); rounded pills/buttons.
 */

import type { ThemeConfig } from '@/types';

export const phoenixThemeConfig: ThemeConfig = {
    colors: {
        // Red sale price + discount badge accent (design: #cd1515 / #C94A4A)
        primary: '#cd1515',
        // Footer + inverse-surface text (design: #202021 bg, #fff text)
        secondary: '#202021',
        // Warm off-white page canvas (design: #ece9e2)
        background: '#ece9e2',
        // Body / nav / price text (design: #131316)
        text: '#131316',
        // Section headings inherit text color; footer uses secondary
        muted: '#202021',
    },
    font: {
        // Jost is EVERYTHING on the reference site (UI, nav, names, prices).
        family: "'Jost', sans-serif",
        familyAr: "'Cairo', 'Tajawal', 'Noto Sans Arabic'",
        baseSize: '15px',
    },
    ui: {
        // Sharp editorial corners on cards (radius 0); buttons/pills are separate.
        borderRadius: '0px',
        buttonStyle: 'outline',
    },
    layout: {
        // Image-first featured card with color swatches + quick-add.
        productCardVariant: 'compact',
        // Floating white pill header centered on the canvas.
        headerVariant: 'centered',
    },
};

export default phoenixThemeConfig;

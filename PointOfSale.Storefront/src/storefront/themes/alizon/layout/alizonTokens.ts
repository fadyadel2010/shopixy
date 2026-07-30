/**
 * Alizon Theme — Layout Tokens (Phase A1.0 scaffold)
 *
 * Central design tokens for the Alizon LAYOUT layer only (header, footer,
 * announcement marquee). These express the design report's spacing scale,
 * container widths, section rhythm, and typography classes so every Alizon
 * layout component stays visually consistent and the values are unit-testable.
 *
 * Colors live in ThemeConfig (themeConfig.ts) and CSS variables; this file is
 * intentionally color-free so layout rhythm can be validated without theme state.
 *
 * Reference: docs/alizon-theme-design-report.md
 *   - Container: max-w-7xl (1280px) with px-4 gutters (matches reference site)
 *   - Display font: Newsreader (hero/headings)
 *   - Body/font: Jost (nav, labels, prices)
 *   - Sharp corners: product cards 0px, category/header 0px, buttons pill
 *   - Red #FF0000 for brand/nav/CTA accent
 *   - Yellow #FFD814 for flash sale / promo buttons
 *   - Dark header #131921 + black footer #121212 bracketing a white page
 */

/** Max content width + gutter, shared across Alizon layout blocks. */
export const ALIZON_CONTAINER = 'mx-auto max-w-7xl px-4';

/** Vertical section rhythm (generous whitespace per reference site). */
export const ALIZON_SECTION_PADDING = 'py-12 md:py-16';

/** Font family utility classes (fonts loaded via index.html). */
export const ALIZON_FONT_DISPLAY = "font-['Newsreader',serif]";
export const ALIZON_FONT_BODY = "font-['Jost',sans-serif]";

/** Nav link typography per design report: Jost 16px, brand red, medium weight. */
export const ALIZON_NAV_LINK = `${ALIZON_FONT_BODY} text-[16px] font-medium`;

/** Micro-label (eyebrow) typography: Jost, uppercase, wide tracking. */
export const ALIZON_EYEBROW = `${ALIZON_FONT_BODY} text-[12px] font-semibold uppercase tracking-[0.32em]`;

/**
 * Resolve nav link active/inactive classes.
 * Pure helper so navigation state can be unit-tested without rendering.
 *
 * Reference site: nav links are red (#FF0000) at all times; active link is
 * solid, inactive is softened.
 */
export const alizonNavLinkClass = (isActive: boolean): string => {
    const base = `${ALIZON_NAV_LINK} transition-colors`;
    return isActive ? `${base} text-[#FF0000]` : `${base} text-[#FF0000]/70 hover:text-[#FF0000]`;
};

/** Alizon accents (hardcoded brand colors per design report). */
export const ALIZON_BRAND_BG = 'bg-[#131921]';   // dark header
export const ALIZON_SALE_BG = 'bg-[#FF0000]';     // discount badge / sale price
export const ALIZON_NEW_BG = 'bg-[#FFD814]';      // flash sale / promo button
export const ALIZON_HERO_BG = 'bg-[#FFFFFF]';    // white page
export const ALIZON_FOOTER_BG = 'bg-[#121212]';  // black footer
export const ALIZON_HEADING = 'text-[#121212]';   // section heading ink

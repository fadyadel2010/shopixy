/**
 * Hype Theme — Layout Tokens (Phase H1.1)
 *
 * Central design tokens for the Hype LAYOUT layer only (header, footer,
 * announcement marquee). These express the design report's spacing scale,
 * container widths, section rhythm, and typography classes so every Hype
 * layout component stays visually consistent and the values are unit-testable.
 *
 * Colors live in ThemeConfig (themeConfig.ts) and CSS variables; this file is
 * intentionally color-free so layout rhythm can be validated without theme state.
 *
 * Reference: docs/hype-electronics-theme-design-report.md
 *   - Container: max-w-7xl (1280px) with px-4 gutters (matches reference site)
 *   - Display font: Newsreader (hero/headings)
 *   - Body/font: Jost (nav, labels, prices)
 *   - Mixed corners: product cards 8px, category/header 0px, buttons pill
 *   - Two oranges: brand #F06736 (nav) vs sale badge #F54E05 (do not merge)
 *   - Cyan #27CDE4 for "New"/custom badges (signature accent)
 *   - Dark hero #141218 + black footer #000000 bracketing a white page
 */

/** Max content width + gutter, shared across Hype layout blocks. */
export const HYPE_CONTAINER = 'mx-auto max-w-7xl px-4';

/** Vertical section rhythm (generous whitespace per reference site). */
export const HYPE_SECTION_PADDING = 'py-12 md:py-16';

/** Font family utility classes (fonts loaded via index.html). */
export const HYPE_FONT_DISPLAY = "font-['Newsreader',serif]";
export const HYPE_FONT_BODY = "font-['Jost',sans-serif]";

/** Nav link typography per design report: Jost 16px, brand orange, medium weight. */
export const HYPE_NAV_LINK = `${HYPE_FONT_BODY} text-[16px] font-medium`;

/** Micro-label (eyebrow) typography: Jost, uppercase, wide tracking. */
export const HYPE_EYEBROW = `${HYPE_FONT_BODY} text-[12px] font-semibold uppercase tracking-[0.32em]`;

/**
 * Resolve nav link active/inactive classes.
 * Pure helper so navigation state can be unit-tested without rendering.
 *
 * Reference site: nav links are orange (#F06736) at all times; active link is
 * solid, inactive is softened.
 */
export const hypeNavLinkClass = (isActive: boolean): string => {
    const base = `${HYPE_NAV_LINK} transition-colors`;
    return isActive ? `${base} text-primary-text` : `${base} text-text hover:text-primary-text`;
};

/** Hype accents (hardcoded brand colors per design report). */
export const HYPE_BRAND_BG = 'bg-[#F06736]';   // nav / CTA accent (--hd-text)
export const HYPE_SALE_BG = 'bg-[#F54E05]';     // discount badge (#F54E05)
export const HYPE_NEW_BG = 'bg-[#27CDE4]';      // "New"/custom badge (#27CDE4)
export const HYPE_HERO_BG = 'bg-[#141218]';    // dark hero band
export const HYPE_FOOTER_BG = 'bg-black';       // black footer
export const HYPE_HEADING = 'text-[#212A2F]';   // section heading ink

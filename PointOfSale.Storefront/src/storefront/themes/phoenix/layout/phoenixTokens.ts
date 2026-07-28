/**
 * Phoenix Sports Theme — Layout Tokens (Phase T1.1)
 *
 * Central design tokens for the Phoenix LAYOUT layer only (header, footer,
 * announcement marquee). These express the design report's spacing scale,
 * container widths, floating-pill header shell, section rhythm, and typography
 * classes so every Phoenix layout component stays visually consistent and the
 * values are unit-testable.
 *
 * Colors live in ThemeConfig (themeConfig.ts) and CSS variables; this file is
 * intentionally color-free (except the dark announcement strip, which has no
 * token home) so layout rhythm can be validated without theme state.
 *
 * Reference: docs/phoenix-sports-theme-design-report.md
 *   - Container: max-w-7xl (1280px) with px-4 gutters (matches reference site)
 *   - Display font: Playfair Display (hero/headings, serif per reference)
 *   - Body/font: Jost (nav, labels, prices, names — measured everywhere)
 *   - Floating white header pill: max-w calc(100%-40px), 19px top margin, 12px radius
 *   - Sharp editorial corners on cards (border-radius 0); pills/buttons rounded
 */

/** Max content width + gutter, shared across Phoenix layout blocks. */
export const PHOENIX_CONTAINER = 'mx-auto max-w-7xl px-4';

/** Floating-pill header outer shell (centers the white pill over the canvas). */
export const PHOENIX_HEADER_SHELL = 'mx-auto max-w-[calc(100%-40px)] mt-[19px]';

/** Vertical section rhythm (generous whitespace per reference site). */
export const PHOENIX_SECTION_PADDING = 'py-12 md:py-16';

/** Font family utility classes (fonts loaded via index.html). */
export const PHOENIX_FONT_DISPLAY = "font-['Playfair_Display',serif]";
export const PHOENIX_FONT_BODY = "font-['Jost',sans-serif]";

/** Nav link typography per design report: Jost 12px uppercase, 0.08em tracking (measured 0.96px). */
export const PHOENIX_NAV_LINK = `${PHOENIX_FONT_BODY} text-[12px] uppercase tracking-[0.08em]`;

/** Micro-label (eyebrow) typography: Jost, uppercase, wide tracking. */
export const PHOENIX_EYEBROW = `${PHOENIX_FONT_BODY} text-[11px] uppercase tracking-[0.24em]`;

/**
 * Resolve nav link active/inactive classes.
 * Phoenix header sits on a white pill, so link ink is black (#000).
 * Pure helper so navigation state can be unit-tested without rendering.
 */
export const phoenixNavLinkClass = (isActive: boolean): string => {
    const base = `${PHOENIX_NAV_LINK} transition-colors`;
    return isActive ? `${base} text-black` : `${base} text-black/60 hover:text-black`;
};

/** Phoenix announcement strip: dark (#212121) band, white uppercase text. */
export const PHOENIX_ACCENT_BG = 'bg-[#212121]';
export const PHOENIX_ACCENT_TEXT = 'text-white';

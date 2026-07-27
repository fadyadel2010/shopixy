/**
 * Pearl Theme — Layout Tokens (Phase T1.1)
 *
 * Central design tokens for the Pearl LAYOUT layer only (header, footer,
 * announcement marquee). These express the design report's spacing scale,
 * container widths, section rhythm, and typography classes so every Pearl
 * layout component stays visually consistent and the values are unit-testable.
 *
 * Colors live in ThemeConfig (themeConfig.ts) and CSS variables; this file is
 * intentionally color-free so layout rhythm can be validated without theme state.
 *
 * Reference: docs/pearl-watches-theme-design-report.md
 *   - Container: max-w-7xl (1280px) with px-4 gutters (matches reference site)
 *   - Display font: Newsreader (hero/headings)
 *   - Body/font: Jost (nav, labels, prices)
 *   - Sharp editorial corners (border-radius 0)
 */

/** Max content width + gutter, shared across Pearl layout blocks. */
export const PEARL_CONTAINER = 'mx-auto max-w-7xl px-4';

/** Vertical section rhythm (generous whitespace per reference site). */
export const PEARL_SECTION_PADDING = 'py-12 md:py-16';

/** Font family utility classes (fonts loaded via index.html in Phase T1.0). */
export const PEARL_FONT_DISPLAY = "font-['Newsreader',serif]";
export const PEARL_FONT_BODY = "font-['Jost',sans-serif]";

/** Nav link typography per design report: Jost 12px uppercase, wide tracking. */
export const PEARL_NAV_LINK = `${PEARL_FONT_BODY} text-[12px] uppercase tracking-[0.28em]`;

/** Micro-label (eyebrow) typography: Jost, uppercase, wide tracking. */
export const PEARL_EYEBROW = `${PEARL_FONT_BODY} text-[11px] uppercase tracking-[0.32em]`;

/**
 * Resolve nav link active/inactive classes.
 * Pure helper so navigation state can be unit-tested without rendering.
 */
export const pearlNavLinkClass = (isActive: boolean): string => {
    const base = `${PEARL_NAV_LINK} transition-colors`;
    return isActive ? `${base} text-white` : `${base} text-white/60 hover:text-white`;
};

/** Pearl accent (red) used by marquee + CTAs. */
export const PEARL_ACCENT_BG = 'bg-[#DC2626]';
export const PEARL_ACCENT_TEXT = 'text-white';

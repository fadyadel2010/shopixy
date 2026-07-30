/**
 * Sharp Jewelry Theme — Layout Tokens (Phase T1.0)
 *
 * Central design tokens for the Sharp Jewelry LAYOUT layer only (header, footer,
 * announcement marquee). These express the design report's spacing scale,
 * container widths, section rhythm, and typography classes so every Sharp
 * layout component stays visually consistent and the values are unit-testable.
 *
 * Colors live in ThemeConfig (themeConfig.ts) and CSS variables; this file is
 * intentionally color-free so layout rhythm can be validated without theme state.
 *
 * Reference: docs/sharp-jewelry-theme-design-report.md
 *   - Container: max-w-7xl (1280px) with px-4 gutters (matches reference site)
 *   - Display font: Playfair Display (newsletter headings)
 *   - Body/font: Instrument Sans (nav, labels, prices)
 *   - Sharp editorial corners (border-radius 0)
 */

/** Max content width + gutter, shared across Sharp layout blocks. */
export const SHARP_CONTAINER = 'mx-auto max-w-7xl px-4';

/** Vertical section rhythm (generous whitespace per reference site). */
export const SHARP_SECTION_PADDING = 'py-12 md:py-16';

/** Font family utility classes (fonts loaded via index.html in Phase T1.0). */
export const SHARP_FONT_DISPLAY = "font-['Playfair Display',serif]";
export const SHARP_FONT_BODY = "font-['Instrument Sans',sans-serif]";

/** Nav link typography per design report: Instrument Sans 14px uppercase, wide tracking. */
export const SHARP_NAV_LINK = `${SHARP_FONT_BODY} text-[14px] uppercase tracking-[0.2em]`;

/** Micro-label (eyebrow) typography: Instrument Sans, uppercase, wide tracking. */
export const SHARP_EYEBROW = `${SHARP_FONT_BODY} text-[11px] uppercase tracking-[0.2em]`;

/**
 * Resolve nav link active/inactive classes.
 * Pure helper so navigation state can be unit-tested without rendering.
 */
export const sharpNavLinkClass = (isActive: boolean): string => {
    const base = `${SHARP_NAV_LINK} transition-colors`;
    return isActive ? `${base} text-white` : `${base} text-white/60 hover:text-white`;
};

/** Sharp accent (red) used by carousel arrows + hover states. */
export const SHARP_ACCENT_BG = 'bg-[#FF5A5A]';
export const SHARP_ACCENT_TEXT = 'text-white';

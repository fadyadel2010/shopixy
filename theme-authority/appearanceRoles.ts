/** Presentation-only roles. Never persist derived values over merchant colors. */
export interface AppearanceColors {
    primary: string;
    secondary: string;
    background: string;
    text: string;
    muted: string;
}

export const isHexColor = (value: unknown): value is string =>
    typeof value === 'string' && /^#(?:[a-f\d]{3}|[a-f\d]{6})$/i.test(value);

export function rgbChannels(hex: string): number[] {
    const value = hex.slice(1);
    const expanded = value.length === 3 ? [...value].map(c => c + c).join('') : value;
    return [0, 2, 4].map(offset => parseInt(expanded.slice(offset, offset + 2), 16));
}

function luminance(hex: string): number {
    const [r, g, b] = rgbChannels(hex).map(channel => {
        const s = channel / 255;
        return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrastRatio(a: string, b: string): number {
    const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
    return (light + 0.05) / (dark + 0.05);
}

export function onColor(background: string): string {
    return contrastRatio('#ffffff', background) >= contrastRatio('#020617', background) ? '#ffffff' : '#020617';
}

function mix(a: string, b: string, weight: number): string {
    const target = rgbChannels(b);
    return '#' + rgbChannels(a).map((c, i) => Math.round(c + (target[i] - c) * weight).toString(16).padStart(2, '0')).join('');
}

/** Preserve the fill/accent; use an accessible shade only when it is text on canvas. */
function readableAccent(accent: string, background: string): string {
    if (contrastRatio(accent, background) >= 4.5) return accent;
    const target = onColor(background);
    for (let step = 1; step <= 100; step++) {
        const candidate = mix(accent, target, step / 100);
        if (contrastRatio(candidate, background) >= 4.5) return candidate;
    }
    return target;
}

export function appearanceRoles(colors: AppearanceColors): Record<string, string> {
    return {
        'on-primary': onColor(colors.primary),
        'on-secondary': onColor(colors.secondary),
        'on-text': onColor(colors.text),
        'primary-text': readableAccent(colors.primary, colors.background),
        'secondary-text': readableAccent(colors.secondary, colors.background),
        surface: mix(colors.background, colors.text, 0.04),
        subtle: mix(colors.background, colors.text, 0.08),
        border: mix(colors.background, colors.text, 0.55),
    };
}

/** Non-blocking, actionable warnings: never silently rewrite chosen text colors. */
export function appearanceContrastWarnings(colors: AppearanceColors): string[] {
    if (!Object.values(colors).every(isHexColor)) return [];
    return (['text', 'muted'] as const).flatMap(role => {
        const ratio = contrastRatio(colors[role], colors.background);
        return ratio < 4.5 ? [`${role}: ${ratio.toFixed(2)}:1 against Background; choose a color with at least 4.5:1 contrast.`] : [];
    });
}

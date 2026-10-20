export const COSMETICS_FONT = "font-['Instrument Sans',sans-serif]";
export const COSMETICS_ACCENT = '#D05D61';
export const COSMETICS_BLUSH = '#E7DFDB';
export const COSMETICS_CONTAINER = 'mx-auto w-full max-w-none px-0';
export const COSMETICS_INNER = 'mx-auto max-w-7xl px-4 sm:px-6 lg:px-10';
export const COSMETICS_NAV_LINK = `${COSMETICS_FONT} text-[13px] font-medium uppercase tracking-[0.12em]`;

export function cosmeticsNavLinkClass(isActive: boolean): string {
    const base = `${COSMETICS_NAV_LINK} transition-opacity duration-200`;
    return isActive ? `${base} text-primary-text underline underline-offset-4` : `${base} text-text hover:text-primary-text`;
}

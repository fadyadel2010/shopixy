export function getThemePreviewToken(search?: string): string | null {
    const resolvedSearch = search ?? (
        typeof window === 'undefined' ? '' : window.location.search
    );
    const token = new URLSearchParams(resolvedSearch).get('themePreviewToken');
    const normalized = token?.trim();

    return normalized ? normalized : null;
}

export function isThemePreviewMode(search?: string): boolean {
    return getThemePreviewToken(search) !== null;
}

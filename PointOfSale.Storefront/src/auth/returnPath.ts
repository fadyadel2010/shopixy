export function resolveStorefrontReturnPath(value: string | null | undefined, fallback = '/'): string {
    if (typeof value !== 'string') {
        return fallback;
    }

    const candidate = value.trim();
    if (
        !candidate.startsWith('/') ||
        candidate.startsWith('//') ||
        candidate.includes('\\') ||
        /[\u0000-\u001f\u007f]/.test(candidate)
    ) {
        return fallback;
    }

    return candidate;
}

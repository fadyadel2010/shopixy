const COUNTRY_CODE_PATTERN = /^[A-Za-z]{2}$/;

export function normalizeCountryCode(countryCode?: string | null): string | null {
    if (!countryCode) {
        return null;
    }

    const trimmedCountryCode = countryCode.trim();
    if (!COUNTRY_CODE_PATTERN.test(trimmedCountryCode)) {
        return null;
    }

    return trimmedCountryCode.toUpperCase();
}

function extractLocaleCountryCode(locale?: string | null): string | null {
    if (!locale) {
        return null;
    }

    const parts = locale
        .split(/[-_]/)
        .map((part) => part.trim())
        .filter(Boolean);

    for (const part of parts.slice(1)) {
        const normalizedCountryCode = normalizeCountryCode(part);
        if (normalizedCountryCode) {
            return normalizedCountryCode;
        }
    }

    return null;
}

export function detectBrowserCountryCode(): string | null {
    if (typeof window === 'undefined') {
        return null;
    }

    const localeCandidates = [
        ...(navigator.languages ?? []),
        navigator.language,
        Intl.DateTimeFormat().resolvedOptions().locale,
    ];

    for (const locale of localeCandidates) {
        const normalizedCountryCode = extractLocaleCountryCode(locale);
        if (normalizedCountryCode) {
            return normalizedCountryCode;
        }
    }

    return null;
}

export function resolveCountryCode(...countryCandidates: Array<string | null | undefined>): string | null {
    for (const countryCandidate of countryCandidates) {
        const normalizedCountryCode = normalizeCountryCode(countryCandidate);
        if (normalizedCountryCode) {
            return normalizedCountryCode;
        }
    }

    return detectBrowserCountryCode();
}

export function containsArabicScript(value: unknown): boolean;
export function containsLatinScript(value: unknown): boolean;
export function getMarketplaceTranslationState(product: {
    isAutoTranslated?: boolean; name?: string; description?: string; category?: string;
    features?: string[]; faqs?: { question?: string; answer?: string }[];
} | null | undefined, language: unknown): { showAutoTranslationBadge: boolean; showPartialTranslationWarning: boolean; categoryUsesFallback: boolean };

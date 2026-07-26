/**
 * useTranslation Hook
 * Phase 10.1: Multi-language Support
 * 
 * React hook for translations with reactive language updates.
 */

import { useCallback } from 'react';
import { useLanguageStore, t } from './languageStore';
import type { SupportedLanguage } from './types';
import { LANGUAGE_CONFIGS } from './types';

interface UseTranslationReturn {
    /** Translate a key with optional parameters */
    t: (key: string, params?: Record<string, string | number>) => string;
    /** Current language code */
    language: SupportedLanguage;
    /** Whether current language is RTL */
    isRTL: boolean;
    /** Current text direction */
    direction: 'ltr' | 'rtl';
    /** Set the current language */
    setLanguage: (language: SupportedLanguage) => void;
    /** Toggle between languages */
    toggleLanguage: () => void;
    /** Get language config */
    getLanguageConfig: () => typeof LANGUAGE_CONFIGS[SupportedLanguage];
}

/**
 * Hook for translations and language state
 * 
 * @example
 * ```tsx
 * const { t, language, isRTL, toggleLanguage } = useTranslation();
 * 
 * return (
 *   <div>
 *     <p>{t('common.loading')}</p>
 *     <button onClick={toggleLanguage}>
 *       {isRTL ? 'English' : 'العربية'}
 *     </button>
 *   </div>
 * );
 * ```
 */
export function useTranslation(): UseTranslationReturn {
    const language = useLanguageStore((state) => state.language);
    const setLanguage = useLanguageStore((state) => state.setLanguage);
    const toggleLanguage = useLanguageStore((state) => state.toggleLanguage);
    const isRTL = useLanguageStore((state) => state.isRTL());
    const direction = useLanguageStore((state) => state.getDirection());

    // Memoized translate function that uses current language
    const translate = useCallback(
        (key: string, params?: Record<string, string | number>) => {
            return t(key, params, language);
        },
        [language]
    );

    const getLanguageConfig = useCallback(() => {
        return LANGUAGE_CONFIGS[language];
    }, [language]);

    return {
        t: translate,
        language,
        isRTL,
        direction,
        setLanguage,
        toggleLanguage,
        getLanguageConfig,
    };
}

/**
 * Get localized content from an object with en/ar fields
 * 
 * @example
 * ```tsx
 * const product = { name: 'Product', nameAr: 'منتج' };
 * const localizedName = getLocalizedField(product, 'name', language);
 * ```
 */
export function getLocalizedField<T extends Record<string, any>>(
    obj: T,
    field: string,
    language: SupportedLanguage
): string {
    if (!obj) return '';

    // Try Arabic field first if language is Arabic
    if (language === 'ar') {
        const arField = `${field}Ar`;
        if (obj[arField] && typeof obj[arField] === 'string' && obj[arField].trim()) {
            return obj[arField];
        }
    }

    // Fall back to English/default field
    const value = obj[field];
    return typeof value === 'string' ? value : '';
}

/**
 * Hook variant that also provides getLocalizedField helper
 */
export function useLocalizedContent() {
    const { language, ...rest } = useTranslation();

    const localize = useCallback(
        <T extends Record<string, any>>(obj: T, field: string) => {
            return getLocalizedField(obj, field, language);
        },
        [language]
    );

    return {
        ...rest,
        language,
        localize,
    };
}

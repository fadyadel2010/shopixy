/**
 * i18n Types
 * Phase 10.1: Multi-language Support
 */

export type SupportedLanguage = 'en' | 'ar';

export const SUPPORTED_LANGUAGES: SupportedLanguage[] = ['en', 'ar'];

export const DEFAULT_LANGUAGE: SupportedLanguage = 'en';

export interface LanguageConfig {
    code: SupportedLanguage;
    name: string;
    nativeName: string;
    direction: 'ltr' | 'rtl';
    icon?: string;
}

export const LANGUAGE_CONFIGS: Record<SupportedLanguage, LanguageConfig> = {
    en: {
        code: 'en',
        name: 'English',
        nativeName: 'English',
        direction: 'ltr',
    },
    ar: {
        code: 'ar',
        name: 'Arabic',
        nativeName: 'العربية',
        direction: 'rtl',
    },
};

/**
 * Translation key type - allows nested keys like 'common.loading'
 */
export type TranslationKey = string;

/**
 * Translation values can be strings or nested objects
 */
export type TranslationValue = string | { [key: string]: TranslationValue };

/**
 * Translation dictionary type
 */
export type TranslationDictionary = Record<string, TranslationValue>;

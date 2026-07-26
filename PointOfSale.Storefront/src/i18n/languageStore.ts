/**
 * Language Store (Zustand)
 * Phase 10.1: Multi-language Support
 * 
 * Manages the current language state and provides language utilities.
 * Persists language preference to localStorage.
 * 
 * CRITICAL:
 * - Language is a VIEW concern, not a data ownership concern
 * - One document → multiple language fields
 * - No duplicate documents per language
 */

import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { SupportedLanguage } from './types';
import { DEFAULT_LANGUAGE, LANGUAGE_CONFIGS, SUPPORTED_LANGUAGES } from './types';
import enTranslations from './locales/en.json';
import arTranslations from './locales/ar.json';

// ============================================================================
// Types
// ============================================================================

interface LanguageState {
    /** Current language code */
    language: SupportedLanguage;
    /** Whether language was explicitly set by user */
    isExplicitlySet: boolean;
}

interface LanguageActions {
    /** Set the current language */
    setLanguage: (language: SupportedLanguage) => void;
    /** Toggle between languages */
    toggleLanguage: () => void;
    /** Get the text direction for current language */
    getDirection: () => 'ltr' | 'rtl';
    /** Check if current language is RTL */
    isRTL: () => boolean;
    /** Get language display name */
    getLanguageName: () => string;
    /** Get native language name */
    getNativeLanguageName: () => string;
}

type LanguageStore = LanguageState & LanguageActions;

// ============================================================================
// Translation Dictionaries
// ============================================================================

const translations: Record<SupportedLanguage, Record<string, any>> = {
    en: enTranslations,
    ar: arTranslations,
};

// ============================================================================
// Helper Functions
// ============================================================================

/**
 * Detect browser language and map to supported language
 */
function detectBrowserLanguage(): SupportedLanguage {
    if (typeof navigator === 'undefined') return DEFAULT_LANGUAGE;

    const browserLang = navigator.language || (navigator as any).userLanguage || '';
    const langCode = browserLang.split('-')[0].toLowerCase();

    if (SUPPORTED_LANGUAGES.includes(langCode as SupportedLanguage)) {
        return langCode as SupportedLanguage;
    }

    return DEFAULT_LANGUAGE;
}

/**
 * Get initial language from URL, cookie, or browser
 */
function getInitialLanguage(): SupportedLanguage {
    // 1. Check URL query parameter
    if (typeof window !== 'undefined') {
        const urlParams = new URLSearchParams(window.location.search);
        const urlLang = urlParams.get('lang');
        if (urlLang && SUPPORTED_LANGUAGES.includes(urlLang as SupportedLanguage)) {
            return urlLang as SupportedLanguage;
        }
    }

    // 2. Check localStorage (handled by persist middleware)
    // 3. Fall back to browser detection
    return detectBrowserLanguage();
}

/**
 * Apply language to document (HTML attributes)
 */
function applyLanguageToDocument(language: SupportedLanguage): void {
    if (typeof document === 'undefined') return;

    const config = LANGUAGE_CONFIGS[language];
    const html = document.documentElement;

    // Set lang and dir attributes
    html.setAttribute('lang', language);
    html.setAttribute('dir', config.direction);

    // Update body class for CSS targeting
    document.body.classList.remove('lang-en', 'lang-ar', 'rtl', 'ltr');
    document.body.classList.add(`lang-${language}`, config.direction);
}

// ============================================================================
// Store Implementation
// ============================================================================

export const useLanguageStore = create<LanguageStore>()(
    persist(
        (set, get) => ({
            language: getInitialLanguage(),
            isExplicitlySet: false,

            setLanguage: (language: SupportedLanguage) => {
                if (!SUPPORTED_LANGUAGES.includes(language)) {
                    console.warn(`[i18n] Unsupported language: ${language}`);
                    return;
                }

                set({ language, isExplicitlySet: true });
                applyLanguageToDocument(language);

                // Update Accept-Language for API calls
                if (typeof document !== 'undefined') {
                    document.cookie = `language=${language}; path=/; max-age=31536000; SameSite=Lax`;
                }
            },

            toggleLanguage: () => {
                const { language, setLanguage } = get();
                const newLang: SupportedLanguage = language === 'en' ? 'ar' : 'en';
                setLanguage(newLang);
            },

            getDirection: () => {
                const { language } = get();
                return LANGUAGE_CONFIGS[language].direction;
            },

            isRTL: () => {
                const { language } = get();
                return LANGUAGE_CONFIGS[language].direction === 'rtl';
            },

            getLanguageName: () => {
                const { language } = get();
                return LANGUAGE_CONFIGS[language].name;
            },

            getNativeLanguageName: () => {
                const { language } = get();
                return LANGUAGE_CONFIGS[language].nativeName;
            },
        }),
        {
            name: 'storefront-language',
            storage: createJSONStorage(() => localStorage),
            partialize: (state) => ({
                language: state.language,
                isExplicitlySet: state.isExplicitlySet,
            }),
            onRehydrateStorage: () => (state) => {
                // Apply language to document after rehydration
                if (state) {
                    applyLanguageToDocument(state.language);
                }
            },
        }
    )
);

// ============================================================================
// Selectors
// ============================================================================

export const selectLanguage = (state: LanguageStore) => state.language;
export const selectIsRTL = (state: LanguageStore) => state.isRTL();
export const selectDirection = (state: LanguageStore) => state.getDirection();

// ============================================================================
// Translation Function
// ============================================================================

/**
 * Get nested translation value by key path
 */
function getNestedValue(obj: Record<string, any>, keyPath: string): string | undefined {
    const keys = keyPath.split('.');
    let value: any = obj;

    for (const key of keys) {
        if (value && typeof value === 'object' && key in value) {
            value = value[key];
        } else {
            return undefined;
        }
    }

    return typeof value === 'string' ? value : undefined;
}

/**
 * Translate a key with optional interpolation
 * 
 * @param key - Translation key (e.g., 'common.loading', 'search.noResultsFor')
 * @param params - Optional parameters for interpolation
 * @param language - Optional language override (defaults to current)
 * @returns Translated string
 */
export function t(
    key: string,
    params?: Record<string, string | number>,
    language?: SupportedLanguage
): string {
    const lang = language || useLanguageStore.getState().language;
    const dict = translations[lang];

    let translation = getNestedValue(dict, key);

    // Fallback to English if translation not found
    if (!translation && lang !== 'en') {
        translation = getNestedValue(translations.en, key);
    }

    // If still not found, return the key
    if (!translation) {
        console.warn(`[i18n] Missing translation: ${key}`);
        return key;
    }

    // Interpolate parameters
    if (params) {
        Object.entries(params).forEach(([paramKey, value]) => {
            translation = translation!.replace(new RegExp(`\\{${paramKey}\\}`, 'g'), String(value));
        });
    }

    return translation;
}

// ============================================================================
// React Hook
// ============================================================================

/**
 * Initialize language on app start
 * Should be called once in App.tsx or main.tsx
 */
export function initializeLanguage(): void {
    const state = useLanguageStore.getState();
    applyLanguageToDocument(state.language);
}

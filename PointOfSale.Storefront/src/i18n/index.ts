/**
 * i18n Module
 * Phase 10.1: Multi-language Support
 * 
 * Main export file for internationalization functionality.
 */

// Types
export type { SupportedLanguage, LanguageConfig, TranslationKey } from './types';
export { SUPPORTED_LANGUAGES, DEFAULT_LANGUAGE, LANGUAGE_CONFIGS } from './types';

// Store
export {
    useLanguageStore,
    selectLanguage,
    selectIsRTL,
    selectDirection,
    t,
    initializeLanguage,
} from './languageStore';

// Hooks
export {
    useTranslation,
    useLocalizedContent,
    getLocalizedField,
} from './useTranslation';

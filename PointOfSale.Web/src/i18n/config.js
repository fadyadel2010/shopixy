import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import { syncDocumentLanguage } from './documentLanguage'
import en from './locales/en.json'
import ar from './locales/ar.json'

const applyDocumentLanguage = (language) => {
    syncDocumentLanguage(language || i18n.resolvedLanguage || i18n.language)
}

i18n
    .use(LanguageDetector)
    .use(initReactI18next)
    .init({
        resources: {
            en: { translation: en },
            ar: { translation: ar },
        },
        supportedLngs: ['en', 'ar'],
        fallbackLng: 'en',
        load: 'languageOnly',
        nonExplicitSupportedLngs: true,
        returnNull: false,
        returnEmptyString: false,
        interpolation: {
            escapeValue: false,
        },
        detection: {
            order: ['localStorage', 'navigator'],
            caches: ['localStorage'],
        },
    })

if (i18n.isInitialized) {
    applyDocumentLanguage()
} else {
    i18n.on('initialized', () => applyDocumentLanguage())
}

i18n.on('languageChanged', applyDocumentLanguage)

export default i18n

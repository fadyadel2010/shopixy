/**
 * AF-15: Affiliate Language Context
 * 
 * Provides language switching (en/ar) with:
 * - Persistent selection via localStorage
 * - RTL direction auto-applied to wrapper
 * - Simple `t('section.key')` accessor
 */
import { createContext, useContext, useState, useCallback, useEffect } from 'react'
import translations from './translations'

const LanguageContext = createContext(null)

const STORAGE_KEY = 'affiliate_lang'

export function LanguageProvider({ children }) {
    const [lang, setLangState] = useState(() => {
        return localStorage.getItem(STORAGE_KEY) || 'en'
    })

    const setLang = useCallback((newLang) => {
        setLangState(newLang)
        localStorage.setItem(STORAGE_KEY, newLang)
    }, [])

    const toggleLang = useCallback(() => {
        setLang(lang === 'en' ? 'ar' : 'en')
    }, [lang, setLang])

    const t = useCallback((path) => {
        const keys = path.split('.')
        let val = translations[lang]
        for (const k of keys) {
            val = val?.[k]
        }
        return val ?? path
    }, [lang])

    const dir = translations[lang]?.dir || 'ltr'
    const isRTL = dir === 'rtl'

    // Apply font for Arabic
    useEffect(() => {
        if (lang === 'ar') {
            const link = document.getElementById('affiliate-arabic-font')
            if (!link) {
                const el = document.createElement('link')
                el.id = 'affiliate-arabic-font'
                el.rel = 'stylesheet'
                el.href = 'https://fonts.googleapis.com/css2?family=Noto+Kufi+Arabic:wght@400;500;600;700;800&display=swap'
                document.head.appendChild(el)
            }
        }
    }, [lang])

    return (
        <LanguageContext.Provider value={{ lang, setLang, toggleLang, t, dir, isRTL }}>
            {children}
        </LanguageContext.Provider>
    )
}

export function useLanguage() {
    const ctx = useContext(LanguageContext)
    if (!ctx) throw new Error('useLanguage must be used within LanguageProvider')
    return ctx
}

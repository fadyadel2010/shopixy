import { afterEach, describe, expect, it } from 'vitest'
import {
    MARKETPLACE_LANGUAGE_STORAGE_KEY,
    buildMarketplaceLanguageUrl,
    replaceMarketplaceLanguagePrefix,
    resolveInitialLanguage,
    setLanguage,
} from './marketplaceLanguage'

describe('marketplaceLanguage', () => {
    afterEach(() => {
        Reflect.deleteProperty(globalThis, 'window')
        Reflect.deleteProperty(globalThis, 'document')
    })

    it('prefers a saved language over the URL prefix', () => {
        installBrowser({ savedLanguage: 'en', browserLanguage: 'ar-EG' })

        expect(resolveInitialLanguage('/ar/product/123')).toBe('en')
    })

    it('uses the URL prefix before browser language when no preference is saved', () => {
        installBrowser({ browserLanguage: 'ar-EG' })

        expect(resolveInitialLanguage('/ar/product/123')).toBe('ar')
    })

    it('preserves the route and query when switching languages', () => {
        expect(buildMarketplaceLanguageUrl('/en/product/123', '?ref=abc', 'ar')).toBe('/ar/product/123?ref=abc')
    })

    it('adds a language prefix when the current path does not include one', () => {
        expect(replaceMarketplaceLanguagePrefix('/product/123', 'ar')).toBe('/ar/product/123')
    })

    it('stores the selected language in local storage and cookie state', () => {
        const browser = installBrowser()

        expect(setLanguage('ar')).toBe('ar')
        expect(browser.storage.get(MARKETPLACE_LANGUAGE_STORAGE_KEY)).toBe('ar')
        expect(browser.document.cookie).toContain('shopixy_marketplace_lang=ar')
    })
})

function installBrowser(options: { savedLanguage?: string | null; browserLanguage?: string } = {}) {
    const storage = new Map<string, string>()
    if (options.savedLanguage) {
        storage.set(MARKETPLACE_LANGUAGE_STORAGE_KEY, options.savedLanguage)
    }

    const windowMock = {
        location: { pathname: '/' },
        navigator: { language: options.browserLanguage ?? 'en-US' },
        localStorage: {
            getItem(key: string) {
                return storage.has(key) ? storage.get(key)! : null
            },
            setItem(key: string, value: string) {
                storage.set(key, value)
            },
        },
    }

    const documentMock = {
        cookie: '',
    }

    Object.defineProperty(globalThis, 'window', {
        configurable: true,
        value: windowMock,
    })

    Object.defineProperty(globalThis, 'document', {
        configurable: true,
        value: documentMock,
    })

    return { storage, document: documentMock }
}

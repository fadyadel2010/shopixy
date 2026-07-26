import { normalizeMarketplaceLanguage } from './marketplaceLanguage'

const ARABIC_SCRIPT_REGEX = /[\u0600-\u06FF]/
const LATIN_SCRIPT_REGEX = /[A-Za-z]/

export function containsArabicScript(value) {
    return typeof value === 'string' && ARABIC_SCRIPT_REGEX.test(value)
}

export function containsLatinScript(value) {
    return typeof value === 'string' && LATIN_SCRIPT_REGEX.test(value)
}

export function getMarketplaceTranslationState(product, language) {
    const currentLanguage = normalizeMarketplaceLanguage(language)

    if (!product || currentLanguage !== 'ar') {
        return {
            showAutoTranslationBadge: Boolean(product?.isAutoTranslated),
            showPartialTranslationWarning: false,
            categoryUsesFallback: false,
        }
    }

    const fields = [
        product.name,
        product.description,
        product.category,
        ...(product.features || []),
        ...flattenFaqContent(product.faqs || []),
    ].filter(Boolean)

    const arabicFieldCount = fields.filter(containsArabicScript).length
    const latinFieldCount = fields.filter((value) => !containsArabicScript(value) && containsLatinScript(value)).length

    return {
        showAutoTranslationBadge: Boolean(product.isAutoTranslated),
        showPartialTranslationWarning: arabicFieldCount > 0 && latinFieldCount > 0,
        categoryUsesFallback:
            typeof product.category === 'string' &&
            !containsArabicScript(product.category) &&
            containsLatinScript(product.category),
    }
}

function flattenFaqContent(faqs) {
    return faqs.flatMap((item) => [item?.question, item?.answer])
}

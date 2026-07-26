import { normalizeDocumentLanguage } from '../i18n/documentLanguage'
import { assertTwoDecimalCurrency, normalizeCurrencyCode, toSafeNumber } from './financialMath'

const DEFAULT_DATE_OPTIONS = {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
}

const DEFAULT_DATE_TIME_OPTIONS = {
    ...DEFAULT_DATE_OPTIONS,
    hour: '2-digit',
    minute: '2-digit',
}

const DEFAULT_TIME_OPTIONS = {
    hour: '2-digit',
    minute: '2-digit',
}

const DEFAULT_NUMBER_OPTIONS = {
    maximumFractionDigits: 2,
}

const DEFAULT_INTEGER_OPTIONS = {
    maximumFractionDigits: 0,
}

const DEFAULT_COMPACT_NUMBER_OPTIONS = {
    ...DEFAULT_INTEGER_OPTIONS,
    notation: 'compact',
}

const DEFAULT_CURRENCY_CODE = 'EGP'

const getDocumentLanguage = () => (
    typeof document !== 'undefined'
        ? document.documentElement?.lang || document.body?.getAttribute('lang') || undefined
        : undefined
)

export const getLocaleForLanguage = (language) => (
    normalizeDocumentLanguage(language || getDocumentLanguage()) === 'ar' ? 'ar-EG' : 'en-US'
)

const normalizeLocale = (locale) => {
    if (!locale) {
        return getLocaleForLanguage()
    }

    if (String(locale).includes('-')) {
        return locale
    }

    return getLocaleForLanguage(locale)
}

const createDateInstance = (value) => {
    if (!value) {
        return null
    }

    const date = value instanceof Date ? value : new Date(value)
    return Number.isNaN(date.getTime()) ? null : date
}

const createNumberFormatter = (locale, options = DEFAULT_NUMBER_OPTIONS) => (
    new Intl.NumberFormat(normalizeLocale(locale), options)
)

const resolveCurrencyCode = (currency) => {
    return assertTwoDecimalCurrency(normalizeCurrencyCode(currency || DEFAULT_CURRENCY_CODE))
}

export const formatDate = (value, locale, options = DEFAULT_DATE_OPTIONS) => {
    const date = createDateInstance(value)
    if (!date) {
        return ''
    }

    return date.toLocaleDateString(normalizeLocale(locale), options)
}

export const formatDateTime = (value, locale, options = DEFAULT_DATE_TIME_OPTIONS) => {
    const date = createDateInstance(value)
    if (!date) {
        return ''
    }

    return date.toLocaleString(normalizeLocale(locale), options)
}

export const formatTime = (value, locale, options = DEFAULT_TIME_OPTIONS) => {
    const date = createDateInstance(value)
    if (!date) {
        return ''
    }

    return date.toLocaleTimeString(normalizeLocale(locale), options)
}

export const formatNumber = (value, locale, options = DEFAULT_NUMBER_OPTIONS) => (
    createNumberFormatter(locale, options).format(toSafeNumber(value))
)

export const formatInteger = (value, locale, options = DEFAULT_INTEGER_OPTIONS) => (
    formatNumber(value, locale, options)
)

export const formatCompactNumber = (value, locale, options = DEFAULT_COMPACT_NUMBER_OPTIONS) => (
    formatNumber(value, locale, options)
)

export const formatCurrency = (value, currency = DEFAULT_CURRENCY_CODE, locale, options = {}) => {
    const normalizedCurrency = resolveCurrencyCode(currency)

    try {
        return createNumberFormatter(locale, {
            style: 'currency',
            currency: normalizedCurrency,
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
            ...options,
        }).format(toSafeNumber(value))
    } catch {
        return createNumberFormatter(locale, {
            style: 'currency',
            currency: DEFAULT_CURRENCY_CODE,
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
            ...options,
        }).format(toSafeNumber(value))
    }
}

export const formatCompactCurrency = (value, currency = DEFAULT_CURRENCY_CODE, locale, options = {}) => (
    formatCurrency(value, currency, locale, {
        notation: 'compact',
        minimumFractionDigits: 0,
        maximumFractionDigits: 1,
        ...options,
    })
)

export const formatSignedCurrency = (value, currency = DEFAULT_CURRENCY_CODE, locale, options = {}) => {
    return formatCurrency(value, currency, locale, { ...options, signDisplay: 'always' })
}

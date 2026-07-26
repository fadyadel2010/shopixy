import { useSettings } from '../contexts/SettingsContext'
import {
    formatCompactNumber as formatCompactNumberValue,
    formatCurrency as formatCurrencyValue,
    formatDate as formatDateValue,
    formatDateTime as formatDateTimeValue,
    formatInteger as formatIntegerValue,
    formatNumber as formatNumberValue,
    formatSignedCurrency as formatSignedCurrencyValue,
    formatTime as formatTimeValue,
} from '../utils/dateFormat'
import { useRTL } from './useRTL'

export function useFormatting() {
    const { settings } = useSettings()
    const { dir, isRTL, language, locale } = useRTL()

    return {
        dir,
        isRTL,
        language,
        locale,
        formatDate: (value, options) => formatDateValue(value, locale, options),
        formatDateTime: (value, options) => formatDateTimeValue(value, locale, options),
        formatTime: (value, options) => formatTimeValue(value, locale, options),
        formatNumber: (value, options) => formatNumberValue(value, locale, options),
        formatInteger: (value, options) => formatIntegerValue(value, locale, options),
        formatCompactNumber: (value, options) => formatCompactNumberValue(value, locale, options),
        formatCurrency: (value, currency = settings?.currencyCode, options) => (
            formatCurrencyValue(value, currency || settings?.currencySymbol, locale, options)
        ),
        formatSignedCurrency: (value, currency = settings?.currencyCode, options) => (
            formatSignedCurrencyValue(value, currency || settings?.currencySymbol, locale, options)
        ),
    }
}

export default useFormatting

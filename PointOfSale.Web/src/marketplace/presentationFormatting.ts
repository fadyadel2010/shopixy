import type { DateValue } from '../utils/dateFormat'

// Presentation-only inputs, supplied by page-level settings/locale authority.
export interface PresentationFormatting {
    dir: 'rtl' | 'ltr'
    isRTL: boolean
    formatDate(value: DateValue, options?: Intl.DateTimeFormatOptions): string
    formatTime(value: DateValue, options?: Intl.DateTimeFormatOptions): string
    formatNumber(value: unknown, options?: Intl.NumberFormatOptions): string
    formatCurrency(value: unknown, currency?: string, options?: Intl.NumberFormatOptions): string
}

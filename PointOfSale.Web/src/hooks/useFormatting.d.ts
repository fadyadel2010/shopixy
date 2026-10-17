import type { DateValue } from '../utils/dateFormat';
export function useFormatting(): {
    dir: 'rtl' | 'ltr'; isRTL: boolean; language: 'ar' | 'en'; locale: 'ar-EG' | 'en-US';
    formatDate(value: DateValue, options?: Intl.DateTimeFormatOptions): string;
    formatDateTime(value: DateValue, options?: Intl.DateTimeFormatOptions): string;
    formatTime(value: DateValue, options?: Intl.DateTimeFormatOptions): string;
    formatNumber(value: unknown, options?: Intl.NumberFormatOptions): string;
    formatInteger(value: unknown, options?: Intl.NumberFormatOptions): string;
    formatCompactNumber(value: unknown, options?: Intl.NumberFormatOptions): string;
    formatCurrency(value: unknown, currency?: string, options?: Intl.NumberFormatOptions): string;
    formatSignedCurrency(value: unknown, currency?: string, options?: Intl.NumberFormatOptions): string;
};
export default useFormatting;

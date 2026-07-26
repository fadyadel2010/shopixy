/**
 * Currency Formatting Utility
 * 
 * Centralized currency formatting using Intl.NumberFormat.
 * Handles different currencies, decimals, and locales.
 * 
 * PHASE 10.4.Y: CQRS Currency Guarantee
 * - Currency is REQUIRED - no default values
 * - If currency is missing, throw error (fail-fast)
 * - Currency must come from StoreConfig (MongoDB projection)
 * 
 * NO hardcoded symbols or formatting logic in components.
 */

/**
 * Validate currency is provided
 */
function validateCurrency(currency: string | undefined | null, functionName: string): string {
    if (!currency || currency.trim() === '') {
        throw new Error(
            `[${functionName}] Currency is required. ` +
            'This indicates the currency was not loaded from StoreConfig.'
        );
    }
    return currency.trim();
}

/**
 * Format amount as currency
 * 
 * @param amount - The numeric amount to format
 * @param currency - ISO 4217 currency code (REQUIRED - e.g., 'USD', 'EUR', 'SAR')
 * @param locale - Optional locale (defaults to 'en-US')
 * @returns Formatted currency string
 * 
 * @example
 * formatMoney(99.99, 'USD') // "$99.99"
 * formatMoney(99.99, 'EUR', 'de-DE') // "99,99 €"
 * formatMoney(99.99, 'SAR', 'ar-SA') // "٩٩٫٩٩ ر.س.‏"
 */
export function formatMoney(
    amount: number,
    currency: string,  // PHASE 10.4.Y: Required - no default
    locale: string = 'en-US'
): string {
    const validCurrency = validateCurrency(currency, 'formatMoney');

    try {
        return new Intl.NumberFormat(locale, {
            style: 'currency',
            currency: validCurrency,
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
        }).format(amount);
    } catch (error) {
        // Fallback for invalid currency codes
        console.warn(`[formatMoney] Invalid currency code: ${validCurrency}`, error);
        return `${validCurrency} ${amount.toFixed(2)}`;
    }
}

/**
 * Format amount as compact currency (for badges, etc.)
 * 
 * @param amount - The numeric amount
 * @param currency - ISO 4217 currency code (REQUIRED)
 * @returns Compact formatted string
 * 
 * @example
 * formatMoneyCompact(1500, 'USD') // "$1.5K"
 * formatMoneyCompact(1500000, 'USD') // "$1.5M"
 */
export function formatMoneyCompact(
    amount: number,
    currency: string,  // PHASE 10.4.Y: Required - no default
    locale: string = 'en-US'
): string {
    const validCurrency = validateCurrency(currency, 'formatMoneyCompact');

    try {
        return new Intl.NumberFormat(locale, {
            style: 'currency',
            currency: validCurrency,
            notation: 'compact',
            maximumFractionDigits: 1,
        }).format(amount);
    } catch (error) {
        console.warn(`[formatMoneyCompact] Invalid currency code: ${validCurrency}`, error);
        return `${validCurrency} ${amount.toFixed(0)}`;
    }
}

/**
 * Get currency symbol only
 * 
 * @param currency - ISO 4217 currency code (REQUIRED)
 * @returns Currency symbol
 * 
 * @example
 * getCurrencySymbol('USD') // "$"
 * getCurrencySymbol('EUR') // "€"
 */
export function getCurrencySymbol(
    currency: string,  // PHASE 10.4.Y: Required - no default
    locale: string = 'en-US'
): string {
    const validCurrency = validateCurrency(currency, 'getCurrencySymbol');

    try {
        const formatter = new Intl.NumberFormat(locale, {
            style: 'currency',
            currency: validCurrency,
        });

        // Extract symbol from formatted output
        const parts = formatter.formatToParts(0);
        const symbolPart = parts.find(part => part.type === 'currency');
        return symbolPart?.value || validCurrency;
    } catch {
        return validCurrency;
    }
}


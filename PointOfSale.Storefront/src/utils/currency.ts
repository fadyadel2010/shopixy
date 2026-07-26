/**
 * Currency Formatting Utilities
 * 
 * PHASE 10.4.X: Currency Authority Lockdown
 * 
 * Provides consistent currency formatting across the storefront.
 * Uses tenant's currency from StoreConfig.
 * 
 * ARCHITECTURAL RULES:
 * - Currency must ALWAYS be provided explicitly
 * - No fallback to "EGP" or "USD"
 * - Throw error if currency is missing
 */

/**
 * Currency symbols mapping (ISO 4217 to symbol)
 */
export const CURRENCY_SYMBOLS: Record<string, string> = {
    'USD': '$',
    'EUR': '€',
    'GBP': '£',
    'EGP': 'EGP ',
    'SAR': 'SAR ',
    'AED': 'AED ',
    'KWD': 'KWD ',
    'QAR': 'QAR ',
    'OMR': 'OMR ',
    'BHD': 'BHD ',
    'JOD': 'JOD ',
    'LBP': 'LBP ',
    'IQD': 'IQD ',
    'SYP': 'SYP ',
    'YER': 'YER ',
    'MAD': 'MAD ',
    'TND': 'TND ',
    'DZD': 'DZD ',
    'LYD': 'LYD ',
    'SDG': 'SDG ',
    'INR': '₹',
    'JPY': '¥',
    'CNY': '¥',
    'KRW': '₩',
    'THB': '฿',
    'TRY': '₺',
    'RUB': '₽',
    'BRL': 'R$',
    'ZAR': 'R',
    'NGN': '₦',
    'PKR': '₨',
};

/**
 * Get currency symbol for a currency code
 */
export function getCurrencySymbol(currencyCode: string): string {
    if (!currencyCode || currencyCode.trim() === '') {
        // PHASE 10.4.X: No fallback - return empty to make missing currency visible
        console.warn('[Currency] getCurrencySymbol called with empty currency code');
        return '??? '; // Make it obvious something is wrong
    }
    return CURRENCY_SYMBOLS[currencyCode.toUpperCase()] || `${currencyCode} `;
}

/**
 * Format a price with the given currency
 * 
 * PHASE 10.4.X: Currency is REQUIRED - no defaults allowed
 */
export function formatPrice(amount: number, currencyCode: string): string {
    if (!currencyCode || currencyCode.trim() === '') {
        // PHASE 10.4.X: Fail fast - no fallback to "EGP"
        console.error('[CRITICAL] formatPrice called without currency code');
        throw new Error(
            'Currency code is required for price formatting. ' +
            'This indicates StoreConfig was not loaded properly.'
        );
    }

    const symbol = getCurrencySymbol(currencyCode);
    const formattedAmount = amount.toFixed(2);

    // For currencies that typically show symbol before amount
    const symbolBefore = ['USD', 'EUR', 'GBP', 'INR', 'JPY', 'CNY', 'KRW', 'THB', 'TRY', 'RUB', 'BRL', 'ZAR', 'NGN', 'PKR'];

    if (symbolBefore.includes(currencyCode.toUpperCase())) {
        return `${symbol}${formattedAmount}`;
    }

    // For Arabic/Middle Eastern currencies, symbol often comes after or as prefix
    return `${symbol}${formattedAmount}`;
}

/**
 * Format a price range (e.g., for filters)
 * 
 * PHASE 10.4.X: Currency is REQUIRED - no defaults allowed
 */
export function formatPriceRange(min: number, max: number, currencyCode: string): string {
    if (!currencyCode || currencyCode.trim() === '') {
        console.error('[CRITICAL] formatPriceRange called without currency code');
        throw new Error(
            'Currency code is required for price range formatting. ' +
            'This indicates StoreConfig was not loaded properly.'
        );
    }
    return `${formatPrice(min, currencyCode)} - ${formatPrice(max, currencyCode)}`;
}


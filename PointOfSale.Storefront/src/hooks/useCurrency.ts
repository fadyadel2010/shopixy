/**
 * useCurrency Hook
 * 
 * PHASE 10.4.X: Currency Authority Lockdown
 * 
 * Provides currency formatting functions that use the tenant's currency
 * from StoreConfig.
 * 
 * ARCHITECTURAL RULES:
 * - Currency MUST come from StoreConfig (MongoDB projection)
 * - If currency is not loaded, this hook THROWS an error
 * - No fallback to "EGP" or "USD" - fail fast
 * - UI must crash loudly if currency is missing
 * 
 * Usage:
 *   const { formatPrice, currency, symbol } = useCurrency();
 *   return <span>{formatPrice(100)}</span>; // "EGP 100.00"
 */

import { useMemo } from 'react';
import { useStoreConfigStore, selectCurrency, selectIsConfigLoaded } from '@/store';
import { formatPrice as formatPriceUtil, getCurrencySymbol, formatPriceRange as formatPriceRangeUtil } from '@/utils/currency';

export interface UseCurrencyResult {
    /** Current currency code (e.g., "EGP", "USD") */
    currency: string;
    /** Currency symbol (e.g., "EGP ", "$") */
    symbol: string;
    /** Format a price with the current currency */
    formatPrice: (amount: number) => string;
    /** Format a price range with the current currency */
    formatPriceRange: (min: number, max: number) => string;
    /** Whether currency is loaded and valid */
    isLoaded: boolean;
}

/**
 * PHASE 10.4.X: Currency Not Configured Error
 * This error is thrown when the currency is not loaded from StoreConfig.
 * The UI should handle this by showing an error state rather than wrong prices.
 */
export class CurrencyNotLoadedError extends Error {
    constructor() {
        super(
            'Storefront currency not loaded. StoreConfig must be fetched before using currency. ' +
            'This is a critical error - displaying prices without currency configuration could result in incorrect charges.'
        );
        this.name = 'CurrencyNotLoadedError';
    }
}

/**
 * Hook to get currency formatting functions using store currency
 * 
 * PHASE 10.4.X: Throws CurrencyNotLoadedError if currency is not configured
 */
export function useCurrency(): UseCurrencyResult {
    const currency = useStoreConfigStore(selectCurrency);
    const isConfigLoaded = useStoreConfigStore(selectIsConfigLoaded);

    // PHASE 10.4.X: Currency Authority Lockdown
    // If config is loaded but currency is empty, this is a critical error
    if (isConfigLoaded && (!currency || currency.trim() === '')) {
        throw new CurrencyNotLoadedError();
    }

    return useMemo(() => ({
        currency: currency || '',
        symbol: getCurrencySymbol(currency || ''),
        formatPrice: (amount: number) => {
            // Additional runtime check - should never hit this if the throw above works
            if (!currency || currency.trim() === '') {
                console.error('[CRITICAL] formatPrice called without currency loaded');
                throw new CurrencyNotLoadedError();
            }
            return formatPriceUtil(amount, currency);
        },
        formatPriceRange: (min: number, max: number) => {
            if (!currency || currency.trim() === '') {
                console.error('[CRITICAL] formatPriceRange called without currency loaded');
                throw new CurrencyNotLoadedError();
            }
            return formatPriceRangeUtil(min, max, currency);
        },
        isLoaded: isConfigLoaded && !!currency && currency.trim() !== '',
    }), [currency, isConfigLoaded]);
}


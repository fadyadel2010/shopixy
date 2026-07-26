/**
 * AF-27: useAffiliateFunnel — React hook for affiliate funnel CTA.
 * 
 * Provides a simple interface for affiliate landing pages to:
 * 1. Server-side inject a product into the cart
 * 2. Redirect directly to checkout
 * 3. Preserve full attribution
 * 
 * Usage in a landing page component:
 * ```tsx
 * const { startFunnel, isLoading, error } = useAffiliateFunnel();
 * 
 * const handleBuyNow = () => {
 *   startFunnel({
 *     productId: 'PRODUCT_GUID',
 *     referralCode: 'ZEZO',     // AF-27E: from URL or props
 *     quantity: 1,
 *   });
 * };
 * ```
 * 
 * FLOW:
 * Click CTA → POST /api/storefront/funnel/start → Redirect to /checkout?cartId=XYZ
 * 
 * SECURITY:
 * - No client-side cart creation
 * - No client-side order creation
 * - Server validates everything
 * - Attribution preserved via URL params (AF-18 pattern)
 */

import { useState, useCallback, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { startAffiliateFunnel, type FunnelStartRequest, type FunnelStartResponse } from '@/services/api/storefrontApi';

interface UseAffiliateFunnelReturn {
    /** Starts the funnel flow: injects product into cart and redirects to checkout */
    startFunnel: (request: FunnelStartRequest) => Promise<void>;
    /** Whether the funnel request is in progress */
    isLoading: boolean;
    /** Error message if the funnel request failed */
    error: string | null;
    /** Last successful response (for confirmation UI if needed) */
    lastResponse: FunnelStartResponse | null;
}

export function useAffiliateFunnel(): UseAffiliateFunnelReturn {
    const navigate = useNavigate();
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [lastResponse, setLastResponse] = useState<FunnelStartResponse | null>(null);
    const requestIdRef = useRef(0);
    const abortControllerRef = useRef<AbortController | null>(null);

    useEffect(() => {
        return () => {
            requestIdRef.current += 1;
            abortControllerRef.current?.abort();
        };
    }, []);

    const startFunnel = useCallback(async (request: FunnelStartRequest) => {
        // Abort any previous request
        abortControllerRef.current?.abort();
        
        // Create new AbortController for this request
        const controller = new AbortController();
        abortControllerRef.current = controller;
        
        const requestId = ++requestIdRef.current;
        setIsLoading(true);
        setError(null);

        try {
            const response = await startAffiliateFunnel(request, controller.signal);

            if (requestId !== requestIdRef.current) {
                return;
            }

            setLastResponse(response);

            // AF-27: Redirect to checkout URL returned by server
            // The URL includes cartId and optionally ref= for attribution
            if (response.checkoutUrl) {
                // Use navigate for SPA routing (checkout is a React route)
                navigate(response.checkoutUrl);
            }
        } catch (err: any) {
            // Ignore aborted requests
            if (err.name === 'CanceledError' || err.name === 'AbortError') {
                return;
            }

            if (requestId !== requestIdRef.current) {
                return;
            }

            const errorMessage = err?.response?.data?.message
                || err?.response?.data?.Message
                || err?.message
                || 'Failed to start checkout. Please try again.';

            const errorCode = err?.response?.data?.code || err?.response?.data?.Code;

            // Provide user-friendly error messages based on error codes
            let displayError = errorMessage;
            switch (errorCode) {
                case 'PRODUCT_NOT_FOUND':
                    displayError = 'This product is no longer available.';
                    break;
                case 'PRODUCT_OUT_OF_STOCK':
                case 'VARIANT_OUT_OF_STOCK':
                    displayError = 'Sorry, this product is currently out of stock.';
                    break;
                case 'PRODUCT_NOT_FOR_SALE':
                    displayError = 'This product is not available for purchase.';
                    break;
                case 'VARIANT_REQUIRED':
                    displayError = 'Please select a product variant before checkout.';
                    break;
                default:
                    break;
            }

            setError(displayError);

            if (import.meta.env.DEV) {
                console.error('[AF-27:FunnelHook] Error:', errorCode, errorMessage, err);
            }
        } finally {
            if (requestId === requestIdRef.current) {
                setIsLoading(false);
            }
        }
    }, [navigate]);

    return {
        startFunnel,
        isLoading,
        error,
        lastResponse,
    };
}

/**
 * AF-12: Affiliate Context Provider
 * 
 * Provides global affiliate mode state management.
 * Reads ?ref= parameter from URL on mount and persists it in memory.
 * 
 * RULES:
 * - Affiliate ref is extracted from URL on first load
 * - Stored in React state only (NOT localStorage)
 * - Automatically appended to all navigation via custom hooks
 * - Request-scoped on backend (middleware validates on each request)
 * 
 * SECURITY:
 * - Ref cannot be changed mid-session (first ref wins)
 * - No client-side activation (backend validates)
 * - Memory-only storage (no persistence)
 */

import React, { createContext, useContext, useEffect, useState, useMemo } from 'react';
import { getConfirmedAffiliateRefCode, subscribeAffiliateRef } from '@/bootstrap/affiliateRefTracker';

interface AffiliateContextValue {
    /** The affiliate referral code (e.g., "AFFLIA9689") */
    affiliateRef: string | null;
    /** Whether affiliate mode is active */
    isAffiliateMode: boolean;
    /** Whether the context has been initialized */
    isInitialized: boolean;
}

const AffiliateContext = createContext<AffiliateContextValue>({
    affiliateRef: null,
    isAffiliateMode: false,
    isInitialized: false,
});

interface AffiliateProviderProps {
    children: React.ReactNode;
}

export const AffiliateProvider: React.FC<AffiliateProviderProps> = ({ children }) => {
    const [affiliateRef, setAffiliateRef] = useState<string | null>(null);
    const [isInitialized, setIsInitialized] = useState(false);

    // Only a server admission verdict may activate presentation/guest affordances.
    useEffect(() => {
        const sync = () => setAffiliateRef(getConfirmedAffiliateRefCode());
        sync();
        setIsInitialized(true);
        return subscribeAffiliateRef(sync);
    }, []);

    const value = useMemo<AffiliateContextValue>(
        () => ({
            affiliateRef,
            isAffiliateMode: !!affiliateRef,
            isInitialized,
        }),
        [affiliateRef, isInitialized]
    );

    return (
        <AffiliateContext.Provider value={value}>
            {children}
        </AffiliateContext.Provider>
    );
};

/**
 * Hook to access affiliate context
 */
export function useAffiliate(): AffiliateContextValue {
    const context = useContext(AffiliateContext);
    if (!context) {
        throw new Error('useAffiliate must be used within AffiliateProvider');
    }
    return context;
}

/**
 * Hook to check if affiliate mode is active
 */
export function useIsAffiliateMode(): boolean {
    const { isAffiliateMode } = useAffiliate();
    return isAffiliateMode;
}

/**
 * Hook to get the affiliate ref code
 */
export function useAffiliateRef(): string | null {
    const { affiliateRef } = useAffiliate();
    return affiliateRef;
}

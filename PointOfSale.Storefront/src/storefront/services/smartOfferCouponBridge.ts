const STORAGE_KEY = 'smartOffer:pendingRedeemToken';
export const SMART_OFFER_REDEEM_EVENT = 'smart-offer:redeem-selected';

function canUseWindow() {
    return typeof window !== 'undefined';
}

function canUseLocalStorage() {
    if (!canUseWindow()) {
        return false;
    }
    try {
        // Test if localStorage is available and writable
        const testKey = '__storage_test__';
        window.localStorage.setItem(testKey, 'test');
        window.localStorage.removeItem(testKey);
        return true;
    } catch {
        return false;
    }
}

export function setPendingSmartOfferRedeemToken(redeemToken: string) {
    if (!canUseWindow()) {
        return;
    }

    const normalizedRedeemToken = redeemToken.trim();
    if (!normalizedRedeemToken) {
        return;
    }

    // Persist to localStorage for cross-context/reload durability
    if (canUseLocalStorage()) {
        try {
            window.localStorage.setItem(STORAGE_KEY, normalizedRedeemToken);
        } catch (error) {
            console.warn('[SmartOfferBridge] Failed to persist redeem token to localStorage:', error);
        }
    }

    // Dispatch signal-only event for same-context synchronization
    // NOTE: Token is NOT included in event detail for security - consumers must use getPendingSmartOfferRedeemToken()
    window.dispatchEvent(new CustomEvent(SMART_OFFER_REDEEM_EVENT));
}

export function getPendingSmartOfferRedeemToken(): string | null {
    if (!canUseWindow()) {
        return null;
    }

    // Read from localStorage for cross-context/reload durability
    if (canUseLocalStorage()) {
        try {
            const storedToken = window.localStorage.getItem(STORAGE_KEY);
            return storedToken?.trim() || null;
        } catch (error) {
            console.warn('[SmartOfferBridge] Failed to read redeem token from localStorage:', error);
        }
    }

    return null;
}

export function clearPendingSmartOfferRedeemToken() {
    if (!canUseWindow()) {
        return;
    }

    // Clear from localStorage
    if (canUseLocalStorage()) {
        try {
            window.localStorage.removeItem(STORAGE_KEY);
        } catch (error) {
            console.warn('[SmartOfferBridge] Failed to clear redeem token from localStorage:', error);
        }
    }
}

export function canEnterCheckout(
    isAuthenticated: boolean,
    enableGuestCheckout: boolean,
    isAffiliateMode: boolean,
): boolean {
    return isAuthenticated || enableGuestCheckout || isAffiliateMode;
}

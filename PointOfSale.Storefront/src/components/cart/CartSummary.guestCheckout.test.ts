import { describe, expect, it } from 'vitest';
import { canEnterCheckout } from '@/features/checkout/cartCheckoutAdmission';
import { checkoutNavigationTarget, withCurrentTenantQuery } from '@/features/checkout/storefrontTenantNavigation';

describe('CartSummary guest checkout admission', () => {
    it('admits ordinary guests only when StoreConfig enables guest checkout', () => {
        expect(canEnterCheckout(false, true, false)).toBe(true);
        expect(canEnterCheckout(false, false, false)).toBe(false);
    });

    it('preserves authenticated and affiliate checkout admission', () => {
        expect(canEnterCheckout(true, false, false)).toBe(true);
        expect(canEnterCheckout(false, false, true)).toBe(true);
    });
});

describe('CartSummary tenant-aware navigation', () => {
    it('preserves explicit development tenant authority for authenticated checkout', () => {
        expect(withCurrentTenantQuery('/checkout', '?tenant=tenant-1&ignored=value'))
            .toBe('/checkout?tenant=tenant-1');
        expect(checkoutNavigationTarget(true, '?tenant=tenant-1'))
            .toBe('/checkout?tenant=tenant-1');
    });

    it('preserves tenant authority in both login and return targets', () => {
        expect(checkoutNavigationTarget(false, '?tenant=tenant-1'))
            .toBe('/login?tenant=tenant-1&return=%2Fcheckout%3Ftenant%3Dtenant-1');
        expect(checkoutNavigationTarget(true, '')).toBe('/checkout');
    });
});

import { describe, expect, it } from 'vitest';
import { canEnterCheckout } from '@/features/checkout/cartCheckoutAdmission';

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

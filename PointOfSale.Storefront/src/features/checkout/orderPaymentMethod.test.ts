import { describe, expect, it } from 'vitest';
import { orderPaymentMethod } from './orderPaymentMethod';

describe('tracking payment method authority', () => {
    it.each([undefined, null, '', 'Cash on delivery', 'الدفع عند الاستلام', 'new-provider', {}])('never infers online or COD from a label or absent code: %s', code => {
        expect(orderPaymentMethod(code)).toBe('unknown');
    });
    it.each([['COD', 'cod'], [' cod ', 'cod'], ['MANUALWALLET', 'manual'], ['STRIPE', 'online'], ['PAYMOB', 'online'], ['FAWRY', 'online']])('uses the canonical %s code', (code, expected) => {
        expect(orderPaymentMethod(code)).toBe(expected);
    });
});

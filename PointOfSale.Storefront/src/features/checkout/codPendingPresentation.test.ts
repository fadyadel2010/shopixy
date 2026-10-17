import { describe, expect, it } from 'vitest';
import { isAwaitingCodCollection, type CanonicalPaymentStatusResponse } from './paymentFlow';
import { t } from '@/i18n';

const status: CanonicalPaymentStatusResponse = {
    orderId: 'qa-order', orderNumber: 'QA-1', status: 'PENDING', orderStatus: 'Pending',
    paymentStatus: 'Pending', paymentMethod: 'COD', canRetryPayment: false, totalAmount: 35, currency: 'EGP',
};

describe('COD pending presentation does not claim payment execution', () => {
    it.each(['Pending', 'CashOnDelivery'])('admits known uncollected COD state %s', (paymentStatus) => {
        expect(isAwaitingCodCollection({ ...status, paymentStatus })).toBe(true);
    });
    it.each(['Collected', 'Paid', 'Settled', 'Refunded', 'Unknown'])('does not invent pending COD from %s', (paymentStatus) => {
        expect(isAwaitingCodCollection({ ...status, paymentStatus })).toBe(false);
    });
    it('fails closed for missing method/status and does not alter canonical evidence', () => {
        expect(isAwaitingCodCollection(null)).toBe(false);
        expect(isAwaitingCodCollection({ ...status, paymentMethod: undefined })).toBe(false);
        expect(isAwaitingCodCollection({ ...status, paymentMethod: 'PAYMOB' })).toBe(false);
        expect(isAwaitingCodCollection({ ...status, status: 'FAILED' })).toBe(false);
        expect(status.paymentStatus).toBe('Pending');
    });
    it('provides both languages without claiming collection or remittance', () => {
        for (const language of ['en', 'ar'] as const) {
            for (const name of ['codTitle', 'codDescription', 'codWaitingTitle', 'codWaitingDescription']) {
                const key = `checkout.pending.${name}`;
                expect(t(key, {}, language)).not.toBe(key);
            }
        }
        expect(t('checkout.pending.codDescription', {}, 'en')).toContain('No customer collection');
    });
});

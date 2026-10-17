import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { t } from '@/i18n';

describe('pre-submission shipping and financial presentation', () => {
    it('does not claim a reservation or successful payment before order creation', () => {
        const source = readFileSync(resolve(process.cwd(), 'src/components/checkout/ReviewStep.tsx'), 'utf8');
        expect(source).not.toContain('Shipping reserved for your order');
        expect(source).not.toContain('You will not be charged twice');
        expect(source).toContain("t('checkout.confidence.shipping')");
        expect(source).toContain("t('checkout.confidence.payment')");
        expect(t('checkout.confidence.shipping', {}, 'en')).toContain('revalidated');
        expect(t('checkout.confidence.payment', {}, 'en')).toContain('separate');
    });

    it('defines truthful pre-submit boundary copy in both languages', () => {
        for (const language of ['en', 'ar'] as const) {
            for (const name of ['pricing', 'shipping', 'payment']) {
                const key = `checkout.confidence.${name}`;
                expect(t(key, {}, language)).not.toBe(key);
            }
        }
    });

    it('reuses canonical localization for purchased shipping and payment names', () => {
        const review = readFileSync(resolve(process.cwd(), 'src/components/checkout/ReviewStep.tsx'), 'utf8');
        const summary = readFileSync(resolve(process.cwd(), 'src/components/checkout/OrderSummary.tsx'), 'utf8');
        expect(review).toContain("getLocalizedField(shippingMethod, 'name', language)");
        expect(review).toContain("getLocalizedField(selectedPaymentMethod, 'displayName', language)");
        expect(summary).toContain("getLocalizedField(shippingMethod, 'name', language)");
    });
});

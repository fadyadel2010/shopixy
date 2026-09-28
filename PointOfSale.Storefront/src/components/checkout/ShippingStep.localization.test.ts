import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { t } from '@/i18n';

const shippingKeys = [
    'addressTitle',
    'fieldsUnavailable',
    'fieldsUnavailableHelp',
    'retryFields',
    'noAdditionalFields',
    'fullName',
    'phoneNumber',
    'phoneHelp',
    'emailAddress',
    'city',
    'area',
    'streetAddress',
    'building',
    'apartment',
    'postalCode',
    'orderNotes',
    'deliveryNotes',
    'methodTitle',
    'noEnabledMethods',
    'noEnabledMethodsHelp',
    'notApplicable',
    'notApplicableHelp',
    'unavailable',
    'unavailableHelp',
    'retryMethods',
    'contextRequired',
    'contextRequiredHelp',
    'noMethods',
    'confirmingAvailability',
    'free',
    'estimatedDeliveryDay',
    'estimatedDeliveryDays',
    'continueToPayment',
];

describe('ShippingStep localization', () => {
    it('defines the complete shipping checkout vocabulary in English and Arabic', () => {
        shippingKeys.forEach((key) => {
            const translationKey = `checkout.shipping.${key}`;
            expect(t(translationKey, {}, 'en')).not.toBe(translationKey);
            expect(t(translationKey, {}, 'ar')).not.toBe(translationKey);
        });

        expect(t('checkout.shipping.estimatedDeliveryDays', { count: 3 }, 'en')).toBe('Estimated delivery: 3 days');
        expect(t('checkout.shipping.estimatedDeliveryDays', { count: 3 }, 'ar')).toBe('التوصيل المتوقع خلال 3 أيام');
    });

    it('uses translations and native grouping semantics instead of English checkout copy', () => {
        const source = readFileSync(resolve(process.cwd(), 'src/components/checkout/ShippingStep.tsx'), 'utf8');

        expect(source).toContain('<fieldset className="min-w-0 border-0 p-0">');
        expect(source).toContain("t('checkout.shipping.continueToPayment')");
        expect(source).toContain("language === 'ar' && method.nameAr?.trim()");
        expect(source).toContain('SYSTEM_FIELD_TRANSLATION_KEYS');
        expect(source).not.toContain('>Shipping Address<');
        expect(source).not.toContain('>Shipping Method<');
        expect(source).not.toContain('Continue to Payment\n');
        expect(source).not.toContain("'Free'");
        expect(source).not.toContain('Estimated delivery:');
    });
});

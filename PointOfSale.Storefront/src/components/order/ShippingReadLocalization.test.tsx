import { renderToStaticMarkup } from 'react-dom/server';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useLanguageStore } from '@/i18n/languageStore';
import { t } from '@/i18n';
import { ShippingAddressCard } from './ShippingAddressCard';
import { ShipmentTracking } from './ShipmentTracking';
import { OrderStatusTimeline } from './OrderStatusTimeline';
import { OrderItemsList } from './OrderItemsList';

// React SSR reads Zustand's immutable initial snapshot, not later setState.
// Supply the selected view language to SSR while retaining the real dictionaries
// and interpolation. Genuine browser checks separately exercise the live hook.
vi.mock('@/i18n', async importOriginal => {
    const actual = await importOriginal<typeof import('@/i18n')>();
    return {
        ...actual,
        useTranslation: () => {
            const language = useLanguageStore.getState().language;
            return { language, t: (key: string, params?: Record<string, string | number>) => actual.t(key, params, language) };
        },
    };
});

afterEach(() => useLanguageStore.setState({ language: 'en' }));

describe('read-only shipping localization', () => {
    it('renders Arabic address, shipment and action labels without modifying authoritative text', () => {
        useLanguageStore.setState({ language: 'ar' });
        const address = Object.freeze({ fullName: 'Synthetic recipient', phone: '15555550105', city: 'Memphis', area: 'TN', addressLine: '70 Original Street' });
        const markup = renderToStaticMarkup(<><ShippingAddressCard address={address} /><ShipmentTracking /></>);
        for (const key of ['checkout.shipping.addressTitle', 'order.tracking.recipient', 'order.tracking.phone', 'order.tracking.address', 'order.tracking.shipmentUnavailable']) {
            const translated = t(key, {}, 'ar');
            expect(translated).not.toBe(key);
            expect(markup).toContain(translated);
        }
        expect(markup).toContain('70 Original Street');
        expect(markup).not.toContain('Shipment information not available yet');
        expect(address.addressLine).toBe('70 Original Street');
    });
    it('localizes timeline accessibility and empty item states in both languages', () => {
        for (const language of ['en', 'ar'] as const) {
            useLanguageStore.setState({ language });
            const markup = renderToStaticMarkup(<><OrderStatusTimeline steps={[{ id: 'pending', label: 'Original event', status: 'completed', icon: '📦' }]} /><OrderItemsList items={[]} currency="USD" /></>);
            expect(markup).toContain(t('order.tracking.progress', {}, language));
            expect(markup).toContain(t('order.tracking.completed', {}, language));
            expect(markup).toContain(t('order.tracking.noItems', {}, language));
            expect(markup).toContain('Original event');
        }
    });
});

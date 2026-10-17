import { expect, it } from 'vitest';
import ar from '../../i18n/locales/ar.json';
import en from '../../i18n/locales/en.json';

it('empty shipment results use genuine Arabic translations, not the English fallback', () => {
    expect(ar.shipping.noShipments).toMatch(/[\u0600-\u06ff]/u);
    expect(ar.shipping.noShipmentsDesc).toMatch(/[\u0600-\u06ff]/u);
    expect(ar.shipping.noShipments).not.toBe(en.shipping.noShipments);
    expect(ar.shipping.noShipmentsDesc).not.toBe(en.shipping.noShipmentsDesc);
});

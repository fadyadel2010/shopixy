// New uniquely labeled negative-only checkout; intended zero orders and stock/
// finance effects. Record its cart/session before any final-checkout request.
async page => {
    const result = await page.evaluate(async () => {
        const { httpClient: h } = await import('/src/services/api/httpClient.ts');
        const marker = 'P8QA20261018-1116-NEGATIVE';
        const old = await h.get('/api/storefront/cart');
        if (old.data.ownerType?.toLowerCase() !== 'customer' || old.data.items.length) throw new Error('Refuse nonempty or unowned cart');
        const added = await h.post('/api/storefront/cart/items', { productId: '15e044bc-eaa1-c905-31a4-658aac4ad14b', quantity: 1 });
        const cartId = added.data.cartId;
        const session = await h.post('/api/storefront/checkout-session', { cartId, source: marker });
        const sessionId = session.data.sessionId;
        await h.patch('/api/storefront/checkout-session/' + sessionId, { customerName: marker, phone: '+201000000099', city: 'QA City', area: 'QA Area', address: 'Negative-only QA, no delivery', country: 'EG', notes: marker });
        const methodId = '0f1a22f9-e017-4443-b94e-d37053fa7bca';
        const selection = await h.post('/api/storefront/shipping-methods/select', { checkoutSessionId: sessionId, shippingMethodId: methodId });
        const price = await h.post('/api/storefront/checkout/pricing', { cartId, shippingMethodId: methodId, paymentMethod: 'COD', shippingCountry: 'EG' });
        const expectedPricing = Object.fromEntries(['subtotal', 'shipping', 'tax', 'discount', 'total', 'couponDiscount', 'promotionDiscount', 'cartPromotionDiscount', 'paymentFee', 'manualDiscount'].map(k => [k, price.data[k] ?? 0]));
        const body = { cartId, checkoutSessionId: sessionId, warehouseResolutionId: selection.data.warehouseResolutionId, warehouseResolutionVersion: selection.data.resolutionVersion, shippingMethodId: methodId, paymentMethod: 'COD',
            shippingAddress: { name: marker, phone: '+201000000099', city: 'QA City', state: 'QA Area', street: 'Negative-only QA, no delivery', country: 'EG' }, customerNotes: marker, expectedPricing };
        sessionStorage.setItem('phase8-negative', JSON.stringify({ marker, cartId, sessionId, methodId, body }));
        return { marker, cartId, sessionId, methodId, expectedPricing, orderRequests: 0, expectedFinal: 'zero orders; original empty cart restored; QA session retained unconverted' };
    });
    return { observedUtc: new Date().toISOString(), ...result };
}

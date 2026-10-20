// Only negative final checkout probes. Unknown response or unexpected committed
// order stops immediately; no valid checkout is submitted by this harness.
async page => {
    const checks = [], failures = [], attempts = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const s = await page.evaluate(() => JSON.parse(sessionStorage.getItem('phase8-negative')));
    const p = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const count = () => p.evaluate(async marker => { const a = (await import('/src/services/api.js')).default; const r = await a.get('/admin/orders?search=' + marker + '&pageSize=50'); return r.data.items.map(x => ({ id: x.id || x.orderId, total: x.total || x.totalAmount })); }, s.marker);
    const sf = (method, path, data) => page.evaluate(async ({ method, path, data }) => {
        const { httpClient } = await import('/src/services/api/httpClient.ts');
        try { const r = await httpClient.request({ method, url: '/api/storefront' + path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data });
    let orders;
    try {
        check('Negative scope has no previous order and canonical quote35', (await count()).length === 0 && s.body.expectedPricing.total === 35);
        await page.evaluate(() => { const s = JSON.parse(sessionStorage.getItem('phase8-negative')); if (s.attempted) throw new Error('Negative probes already attempted; query instead of rerunning'); s.attempted = true; sessionStorage.setItem('phase8-negative', JSON.stringify(s)); });
        const forged = await sf('post', '/checkout', { ...s.body, expectedPricing: { ...s.body.expectedPricing, subtotal: 1, total: 11 } });
        attempts.push({ label: 'consistent-forged-total', status: forged.status, code: forged.data?.code, orderId: forged.data?.orderId });
        check('Server rejects internally consistent underpriced total before persistence', forged.status === 409, attempts[0]);
        check('Fresh observer after forged total finds zero orders', (await count()).length === 0);
        const quantity = await sf('put', '/cart/items/15e044bc-eaa1-c905-31a4-658aac4ad14b', { quantity: 2 });
        check('Owned cart changes after accepted preview', quantity.status === 200);
        const selection = await sf('post', '/shipping-methods/select', { checkoutSessionId: s.sessionId, shippingMethodId: s.methodId });
        s.body.warehouseResolutionId = selection.data.warehouseResolutionId; s.body.warehouseResolutionVersion = selection.data.resolutionVersion;
        const stale = await sf('post', '/checkout', s.body);
        attempts.push({ label: 'changed-cart-old-price', status: stale.status, code: stale.data?.code, orderId: stale.data?.orderId });
        check('Changed cart cannot submit previously accepted35 quote', stale.status === 409, attempts[1]);
        const price = await sf('post', '/checkout/pricing', { cartId: s.cartId, shippingMethodId: s.methodId, paymentMethod: 'COD', shippingCountry: 'EG' });
        check('Fresh changed-cart price is server60', price.status === 200 && price.data.subtotal === 50 && price.data.total === 60);
        const pricing = Object.fromEntries(Object.keys(s.body.expectedPricing).map(k => [k, price.data[k] ?? 0]));
        const field = await sf('post', '/checkout', { ...s.body, expectedPricing: pricing, customFields: [{ fieldId: '11111111-1111-4111-8111-111111111111', value: 'forged unknown field' }] });
        attempts.push({ label: 'unknown-checkout-definition', status: field.status, code: field.data?.code, orderId: field.data?.orderId });
        check('Server reloads checkout definitions and rejects unknown field', [400, 422].includes(field.status), attempts[2]);
        orders = await count(); check('Final independent exact-scope observer finds no committed order', orders.length === 0);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); orders = await count(); }
    finally {
        const cart = await sf('get', '/cart');
        if (cart.status === 200 && cart.data.cartId === s.cartId && cart.data.items.length === 1 && cart.data.items[0].productId === '15e044bc-eaa1-c905-31a4-658aac4ad14b') {
            await sf('delete', '/cart'); const observed = await sf('get', '/cart');
            checks.push({ name: 'Negative-only task cart cleaned; customer ownership preserved', passed: observed.status === 200 && observed.data.items.length === 0 && observed.data.ownerType?.toLowerCase() === 'customer' });
        }
    }
    return { observedUtc: new Date().toISOString(), marker: s.marker, cartId: s.cartId, sessionId: s.sessionId, attempts, orders, checks, failures };
}

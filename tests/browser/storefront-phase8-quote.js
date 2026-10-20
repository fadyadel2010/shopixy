// Continue only the recorded cart/coupon. No coupon recreation or order submit.
async page => {
    const scenario = await page.evaluate(() => JSON.parse(sessionStorage.getItem('phase8-scenario')));
    const checks = [], failures = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const adminPage = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const admin = (method, path, data) => adminPage.evaluate(async ({ method, path, data }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data });
    const sf = (method, path, data) => page.evaluate(async ({ method, path, data }) => {
        const { httpClient } = await import('/src/services/api/httpClient.ts');
        try { const r = await httpClient.request({ method, url: '/api/storefront' + path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data });
    try {
        check('Continue only manifested scenario before submission', scenario.marker === 'P8QA20261018-1116' && !!scenario.cartId && !!scenario.couponId && !scenario.submissionAttempted);
        const cart = await sf('get', '/cart');
        check('Recorded customer cart has exactly the owned one line', cart.status === 200 && cart.data.cartId === scenario.cartId && cart.data.items.length === 1);
        const coupon = await admin('get', '/coupons/' + scenario.couponId);
        check('Exact recorded coupon still belongs to scope', coupon.status === 200 && coupon.data.code === scenario.marker);
        const applied = await sf('post', '/cart/apply-coupon', { code: scenario.marker });
        check('Real cart accepts fixed5 single discount', applied.status === 200 && applied.data.totalDiscount === 5, { status: applied.status, discount: applied.data?.totalDiscount });
        const methods = await admin('get', '/shippingmethods?activeOnly=true');
        const method = methods.data.find(x => x.id === '0f1a22f9-e017-4443-b94e-d37053fa7bca');
        check('Existing Manual method available', !!method, { fulfillmentMode: method?.fulfillmentMode, carrierCode: method?.carrierCode });
        scenario.methodId = method.id;
        if (!scenario.sessionId) {
            const session = await sf('post', '/checkout-session', { cartId: scenario.cartId, source: scenario.marker });
            check('Recorded SQL checkout session created once', session.status === 200 && !!session.data.sessionId);
            scenario.sessionId = session.data.sessionId;
            await page.evaluate(s => sessionStorage.setItem('phase8-scenario', JSON.stringify(s)), scenario);
        }
        const saved = await sf('patch', '/checkout-session/' + scenario.sessionId, { customerName: scenario.marker, phone: '+201000000099', city: 'QA City', area: 'QA Area', address: 'Synthetic Phase8 - no real delivery', country: 'EG', notes: scenario.marker, cartTotal: 0.01, currency: 'USD' });
        check('Progressive save ignores forged amounts', saved.status === 200, { status: saved.status });
        const selection = await sf('post', '/shipping-methods/select', { checkoutSessionId: scenario.sessionId, shippingMethodId: method.id });
        check('SQL shipping warehouse selection accepted', selection.status === 200 && !!selection.data.warehouseResolutionId);
        const price = await sf('post', '/checkout/pricing', { cartId: scenario.cartId, shippingMethodId: method.id, paymentMethod: 'COD', shippingCountry: 'EG', couponCode: scenario.marker, total: 0.01, discount: 9999, currency: 'USD' });
        check('Canonical pricing is EGP25-5+10=30, not browser amounts', price.status === 200 && price.data.subtotal === 25 && price.data.discount === 5 && price.data.total === 30 && cart.data.currency === 'EGP', { pricing: price.data, cartCurrency: cart.data.currency });
        scenario.expectedPricing = Object.fromEntries(['subtotal', 'shipping', 'tax', 'discount', 'total', 'couponDiscount', 'promotionDiscount', 'cartPromotionDiscount', 'paymentFee', 'manualDiscount'].map(k => [k, price.data[k] ?? 0]));
        scenario.body = { cartId: scenario.cartId, checkoutSessionId: scenario.sessionId, warehouseResolutionId: selection.data.warehouseResolutionId, warehouseResolutionVersion: selection.data.resolutionVersion, shippingMethodId: method.id, paymentMethod: 'COD', couponCode: scenario.marker,
            shippingAddress: { name: scenario.marker, phone: '+201000000099', city: 'QA City', state: 'QA Area', street: 'Synthetic Phase8 - no real delivery', country: 'EG' }, customerNotes: scenario.marker + ' internal QA only; no physical cash or delivery', expectedPricing: scenario.expectedPricing };
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    await page.evaluate(s => sessionStorage.setItem('phase8-scenario', JSON.stringify(s)), scenario);
    return { observedUtc: new Date().toISOString(), ...scenario, body: undefined, checks, failures, submitted: false };
}

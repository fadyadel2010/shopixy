// One new internal QA scope only. Refuse nonempty/unowned carts and duplicate scope.
// No checkout submission, provider action, account/config change, or model request.
async page => {
    const marker = 'P8QA20261018-1116';
    const checks = [], failures = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const adminPage = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const admin = (method, path, data, version) => adminPage.evaluate(async ({ method, path, data, version }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data, headers: version ? { 'If-Match': version } : {} }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data, version });
    const sf = (method, path, data) => page.evaluate(async ({ method, path, data }) => {
        const { httpClient } = await import('/src/services/api/httpClient.ts');
        try { const r = await httpClient.request({ method, url: '/api/storefront' + path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data });
    let scenario = { marker, intendedFinal: 'one retained completed/settled synthetic COD order; no real delivery/cash; owned coupon archived; cart empty' };
    try {
        await page.context().route('**/api/storefront/chat**', r => r.abort('blockedbyclient'));
        const accounts = await admin('get', '/admin/whatsapp/accounts');
        check('WhatsApp stays disabled; no account mutation', accounts.status === 200 && accounts.data.every(x => !x.enabled));
        const existing = await admin('get', '/admin/orders?search=' + marker + '&pageSize=50');
        check('No committed order already exists in unique scope', existing.status === 200 && existing.data.items.length === 0);
        const me = await sf('get', '/auth/me');
        check('Password login has current server customer identity', me.status === 200 && !!me.data.id, { customerId: me.data.id });
        const before = await sf('get', '/cart');
        check('Real customer-owned cart starts empty', before.status === 200 && before.data.ownerType?.toLowerCase() === 'customer' && before.data.items.length === 0);
        const priorCoupons = await admin('get', '/coupons?search=' + marker);
        check('No previous coupon in this scope', priorCoupons.status === 200 && priorCoupons.data.length === 0);
        const coupon = await admin('post', '/coupons', { code: marker, discountType: 'FixedAmount', discountValue: 5, isActive: true, usageLimit: 2, usageLimitPerCustomer: 1 });
        check('Create exact synthetic fixed-value coupon', coupon.status === 201 && !!coupon.data.id, { couponId: coupon.data?.id });
        scenario.couponId = coupon.data.id;
        await page.goto('http://ahramstore.localhost:5174/?utm_source=p8qa&utm_medium=internal-qa&utm_campaign=' + marker, { waitUntil: 'domcontentloaded' });
        await page.locator('main').waitFor();
        const products = await sf('get', '/products/discover?pageSize=20');
        const product = products.data.items.find(x => x.slug === 'cert-media-simple-20261007');
        check('Supported published product found through discovery', products.status === 200 && !!product, { productId: product?.id || product?.productId });
        const added = await sf('post', '/cart/items', { productId: product.id || product.productId, quantity: 1 });
        check('Authoritative cart accepts one product', added.status === 200 && added.data.items.length === 1);
        scenario.cartId = added.data.cartId;
        const applied = await sf('post', '/cart/apply-coupon', { code: marker });
        check('Cart coupon accepted without stacking', applied.status === 200 && applied.data.totalDiscount === 5, { subtotal: applied.data?.subtotal, discount: applied.data?.totalDiscount });
        const methods = await admin('get', '/shippingmethods?activeOnly=true');
        const method = methods.data.find(x => x.id === '0f1a22f9-e017-4443-b94e-d37053fa7bca' && x.fulfillmentMode === 'Manual');
        check('Existing Manual shipping method selected without changing it', methods.status === 200 && !!method);
        scenario.methodId = method.id;
        const session = await sf('post', '/checkout-session', { cartId: scenario.cartId, source: marker });
        check('New SQL checkout session created', session.status === 200 && !!session.data.sessionId);
        scenario.sessionId = session.data.sessionId;
        const saved = await sf('patch', '/checkout-session/' + scenario.sessionId, { customerName: marker, phone: '+201000000099', city: 'QA City', area: 'QA Area', address: 'Synthetic Phase8 - no real delivery', country: 'EG', notes: marker, cartTotal: 0.01, currency: 'USD' });
        check('Progressive session save uses authoritative totals', saved.status === 200, { status: saved.status });
        const selection = await sf('post', '/shipping-methods/select', { checkoutSessionId: scenario.sessionId, shippingMethodId: method.id });
        check('Server-owned warehouse selection accepted', selection.status === 200 && !!selection.data.warehouseResolutionId);
        const price = await sf('post', '/checkout/pricing', { cartId: scenario.cartId, shippingMethodId: method.id, paymentMethod: 'COD', shippingCountry: 'EG', couponCode: marker, total: 0.01, discount: 9999, currency: 'USD' });
        check('Authoritative price ignores forged body amounts/currency', price.status === 200 && price.data.subtotal === 25 && price.data.discount === 5 && price.data.total === 30 && added.data.currency === 'EGP', { pricing: price.data });
        scenario.expectedPricing = Object.fromEntries(['subtotal', 'shipping', 'tax', 'discount', 'total', 'couponDiscount', 'promotionDiscount', 'cartPromotionDiscount', 'paymentFee', 'manualDiscount'].map(k => [k, price.data[k] ?? 0]));
        scenario.body = { cartId: scenario.cartId, checkoutSessionId: scenario.sessionId, warehouseResolutionId: selection.data.warehouseResolutionId, warehouseResolutionVersion: selection.data.resolutionVersion, shippingMethodId: method.id, paymentMethod: 'COD', couponCode: marker,
            shippingAddress: { name: marker, phone: '+201000000099', city: 'QA City', state: 'QA Area', street: 'Synthetic Phase8 - no real delivery', country: 'EG' }, customerNotes: marker + ' internal QA only; no physical cash or delivery', expectedPricing: scenario.expectedPricing };
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    await page.evaluate(s => sessionStorage.setItem('phase8-scenario', JSON.stringify(s)), scenario);
    return { observedUtc: new Date().toISOString(), ...scenario, body: undefined, checks, failures, submitted: false };
}

// Actual configured customer cart and checkout session. No order or message.
async page => {
    const checks = [], failures = [];
    const prefix = 'P4QA' + Date.now();
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const storefront = await page.context().newPage();
    const sf = (method, path, data) => storefront.evaluate(async ({ method, path, data }) => {
        const { httpClient } = await import('/src/services/api/httpClient.ts');
        try { const r = await httpClient.request({ method, url: '/api' + path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data });
    let sessionId, cartId;
    try {
        await storefront.goto('http://ahramstore.localhost:5174/cart');
        await storefront.locator('main').waitFor();
        const before = await sf('get', '/storefront/cart');
        check('Existing customer cart is authoritative and empty', before.status === 200 && before.data.ownerType.toLowerCase() === 'customer' && before.data.items.length === 0);
        const products = await sf('get', '/storefront/products/discover?pageSize=20');
        let cart;
        for (const product of products.data.items || []) {
            const r = await sf('post', '/storefront/cart/items', { productId: product.id || product.productId, quantity: 1 });
            if (r.status === 200) { cart = r.data; break; }
        }
        check('Current product accepted by actual cart service', cart?.items.length === 1);
        cartId = cart.cartId;
        await storefront.reload();
        await storefront.getByText(cart.items[0].name, { exact: true }).first().waitFor();
        check('Actual cart UI shows current basket', true, { cartId, subtotal: cart.subtotal, currency: cart.currency });
        const create = await sf('post', '/storefront/checkout-session', { cartId, source: prefix, deviceType: 'Desktop', cartTotal: .01, currency: 'USD', itemsSnapshotJson: '[]' });
        check('Checkout session created through hosted authority', create.status === 200, { status: create.status });
        sessionId = create.data.sessionId;
        const update = await sf('patch', '/storefront/checkout-session/' + sessionId, { customerName: prefix, phone: '+201000000099', city: 'QA City', area: 'QA Area', address: 'Synthetic QA address', country: 'EG', notes: prefix, cartTotal: .01, currency: 'USD' });
        check('Progressive save accepted', update.status === 200);
        const detail = await page.evaluate(async id => {
            const api = (await import('/src/services/api.js')).default;
            const r = await api.get('/admin/abandoned-checkouts/' + id);
            return { status: r.data.status, cartTotal: r.data.cartTotal, currency: r.data.currency, source: r.data.source };
        }, sessionId);
        check('Fresh Admin readback ignores forged snapshot totals', detail.status === 'Active' && detail.cartTotal === cart.grandTotal && detail.currency === cart.currency && detail.source === prefix, detail);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally { await storefront.close(); }
    return { prefix, sessionId, cartId, checks, failures, retainedForRecovery: !!sessionId };
}

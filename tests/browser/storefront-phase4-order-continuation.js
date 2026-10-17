// Continue the already-committed UI order. Never repeats its Place Order action.
async page => {
    const prefix = 'P4QA1792177832429', firstId = 'ee42f429-fa3d-46b6-988a-b900cc3e8648';
    const checks = [], failures = [], orders = [{ id: firstId, label: 'recovered-cancel' }];
    let storefront;
    const check = (name, passed, details = {}) => { checks.push({ name, passed: !!passed, ...details }); if (!passed) throw new Error(name); };
    const admin = (method, path, data, version) => page.evaluate(async ({ method, path, data, version }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data, headers: version ? { 'If-Match': version } : {} }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message, error: e.response?.data?.error } }; }
    }, { method, path, data, version });
    const read = async path => { const r = await admin('get', path); if (r.status !== 200) throw new Error('Admin read failed ' + r.status); return r.data; };
    const sf = (method, path, data) => storefront.evaluate(async ({ method, path, data }) => {
        const { httpClient } = await import('/src/services/api/httpClient.ts');
        try { const r = await httpClient.request({ method, url: '/api' + path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data });
    const summary = x => ({ orderId: x.orderId, status: x.status, paymentStatus: x.paymentStatus, fulfillmentStatus: x.fulfillmentStatus, financialPostingStatus: x.financialPostingStatus, fulfillmentMode: x.fulfillmentMode, total: x.totals?.total || x.totalAmount });
    try {
        await page.goto('http://localhost:5173/ecommerce/orders/' + firstId);
        await page.getByText('ORD-C9C5-2026-01054', { exact: true }).first().waitFor();
        let first = await read('/admin/orders/' + firstId);
        check('Existing UI-created synthetic order has expected retained lifecycle', ['Pending', 'OnHold', 'Cancelled'].includes(first.status), summary(first));
        const authority = await read('/orders/' + firstId + '/fulfillment/actions');
        check('Backend supplies fulfillment authority for the synthetic order', authority.orderId === firstId);
        const converted = await read('/admin/abandoned-checkouts/1efa806b-9dd3-4118-a40e-df51b2e6f025');
        check('Recovered checkout converted to this exact order', converted.status === 'ConvertedToOrder' && converted.convertedOrderId === firstId);
        if (first.status !== 'Cancelled') {
        const invalid = await admin('post', '/orders/' + firstId + '/fulfillment/complete', {});
        check('Premature completion rejected', invalid.status >= 400, { status: invalid.status });
        const bypass = await admin('put', '/admin/orders/' + firstId + '/status', { newStatus: 'Completed', reason: prefix }, first.rowVersion);
        check('Direct lifecycle bypass rejected', bypass.status >= 400, { status: bypass.status });
        const competing = await Promise.all([1, 2].map(() => admin('put', '/admin/orders/' + firstId + '/status', { newStatus: 'OnHold', reason: prefix }, first.rowVersion)));
        check('Concurrent status actions admit one version and reject stale competitor', competing.filter(x => x.status === 200).length === 1 && competing.some(x => x.status === 409), { statuses: competing.map(x => x.status) });
        const cancel = await admin('post', '/admin/orders/' + firstId + '/cancel', { reason: prefix + ' QA cancellation' });
        check('Supported cancellation accepted', cancel.status === 200);
        for (let i = 0; i < 90; i++) { first = await read('/admin/orders/' + firstId); if (first.status === 'Cancelled') break; await page.waitForTimeout(500); }
        }
        check('Cancellation persisted', first.status === 'Cancelled', summary(first));
        const repeatCancel = await admin('post', '/admin/orders/' + firstId + '/cancel', { reason: prefix + ' replay' });
        check('Repeated cancellation is idempotent', repeatCancel.status === 200 && (await read('/admin/orders/' + firstId)).status === 'Cancelled');
        const account = (await read('/admin/whatsapp/accounts'))[0];
        check('External messaging remains disabled before another synthetic order', !account.enabled);
        const existingOrders = await read('/admin/orders?search=' + prefix + '&pageSize=50');
        check('No second synthetic order is already committed; otherwise stop and resume its recorded ID', existingOrders.items.length === 1 && existingOrders.items[0].orderId === firstId);
        storefront = await page.context().newPage();
        await storefront.goto('http://ahramstore.localhost:5174/cart'); await storefront.locator('main').waitFor();
        check('UI order submission cleared the current customer cart', (await sf('get', '/storefront/cart')).data.items.length === 0);
        const product = { productId: '03144bba-1880-c1d6-3062-997a1006feca', variantId: '1435b729-5fbe-48f1-a306-2e12051ad24b' };
        const added = await sf('post', '/storefront/cart/items', { productId: product.productId, variantId: product.variantId, quantity: 1 });
        check('Second synthetic cart accepted', added.status === 200, { status: added.status });
        const session = await sf('post', '/storefront/checkout-session', { cartId: added.data.cartId, source: prefix + 'complete' });
        const methodId = '0f1a22f9-e017-4443-b94e-d37053fa7bca';
        const select = await sf('post', '/storefront/shipping-methods/select', { checkoutSessionId: session.data.sessionId, shippingMethodId: methodId });
        check('Current server warehouse admission succeeds', select.status === 200, { status: select.status, code: select.data?.code });
        const price = await sf('post', '/storefront/checkout/pricing', { cartId: added.data.cartId, shippingMethodId: methodId, paymentMethod: 'COD', shippingCountry: 'EG' });
        check('Current authoritative pricing succeeds', price.status === 200, { pricing: price.data });
        const body = { cartId: added.data.cartId, checkoutSessionId: session.data.sessionId, warehouseResolutionId: select.data.warehouseResolutionId, warehouseResolutionVersion: select.data.resolutionVersion, shippingMethodId: methodId, paymentMethod: 'COD', shippingAddress: { name: prefix, phone: '+201000000099', city: 'QA City', state: 'QA Area', street: 'Synthetic QA address', country: 'EG' }, customerNotes: prefix + ' complete', expectedPricing: price.data };
        const forged = await sf('post', '/storefront/checkout', { ...body, expectedPricing: { ...price.data, total: .01 } });
        check('Final server guard rejects forged payable total', forged.status === 409, { status: forged.status, code: forged.data?.code });
        const create = await sf('post', '/storefront/checkout', body);
        check('Second synthetic order persists through actual checkout', create.status === 200, { status: create.status, code: create.data?.code });
        const secondId = create.data.orderId; orders.push({ id: secondId, label: 'manual-complete' });
        const replay = await sf('post', '/storefront/checkout', body);
        check('Identical checkout replay returns same order', replay.status === 200 && replay.data.orderId === secondId, { status: replay.status });
        for (const action of ['confirm', 'pack', 'out-for-delivery', 'delivered']) {
            const r = await admin('post', '/orders/' + secondId + '/fulfillment/' + action, { deliveryReference: prefix, recipientName: 'Synthetic QA' });
            check('Manual fulfillment ' + action, r.status === 200, { status: r.status, message: r.data?.message, code: r.data?.code });
        }
        const unpaid = await admin('post', '/orders/' + secondId + '/fulfillment/complete', {});
        check('Delivered unpaid COD still cannot complete', unpaid.status >= 400, { status: unpaid.status });
        const collected = await admin('post', '/payments/lifecycle/' + secondId + '/cod-delivered', { deliveryReference: prefix });
        check('Internal synthetic COD collection accepted', collected.status === 200, { status: collected.status, message: collected.data?.message, code: collected.data?.code });
        let final;
        for (let i = 0; i < 30; i++) { const actions = await read('/orders/' + secondId + '/fulfillment/actions'); final = await read('/admin/orders/' + secondId); if (actions.canComplete) break; await page.waitForTimeout(500); }
        check('Backend financial authority admits completion after collection', (await read('/orders/' + secondId + '/fulfillment/actions')).canComplete, summary(final));
        const complete = await admin('post', '/orders/' + secondId + '/fulfillment/complete', {});
        check('Financially admitted completion succeeds', complete.status === 200, { status: complete.status });
        const repeat = await admin('post', '/orders/' + secondId + '/fulfillment/complete', {});
        check('Repeated completion is idempotent', repeat.status === 200);
        final = await read('/admin/orders/' + secondId);
        check('Fresh readback is Completed with separate Collected payment state', final.status === 'Completed' && final.paymentStatus === 'Collected', summary(final));
        await page.goto('http://localhost:5173/ecommerce/orders/' + secondId);
        await page.getByText(final.orderNumber, { exact: true }).first().waitFor();
        await page.getByText('Completed', { exact: true }).first().waitFor();
        await sf('delete', '/storefront/cart');
        check('Customer cart cleanup independently reads empty', (await sf('get', '/storefront/cart')).data.items.length === 0);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally { if (storefront) await storefront.close(); }
    return { prefix, orders, checks, failures };
}

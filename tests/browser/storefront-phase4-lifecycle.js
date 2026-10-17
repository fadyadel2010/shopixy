// Hosted recovery -> current cart -> authoritative order -> lifecycle.
// External WhatsApp is disabled through supported account controls while orders
// are created. No payment gateway, carrier booking or message is invoked.
async page => {
    const checks = [], failures = [], orders = [], requests = [];
    const check = (name, passed, details = {}) => { checks.push({ name, passed: !!passed, ...details }); if (!passed) throw new Error(name); };
    let storefront, originalAccount, paused = false, prefix, sessionId;
    const admin = (method, path, data, rowVersion) => page.evaluate(async ({ method, path, data, rowVersion }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data, headers: rowVersion ? { 'If-Match': rowVersion } : {} }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message, error: e.response?.data?.error } }; }
    }, { method, path, data, rowVersion });
    const sf = (method, path, data) => storefront.evaluate(async ({ method, path, data }) => {
        const { httpClient } = await import('/src/services/api/httpClient.ts');
        try { const r = await httpClient.request({ method, url: '/api' + path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data });
    const read = async path => { const r = await admin('get', path); if (r.status !== 200) throw new Error('Admin read failed: ' + r.status); return r.data; };
    const orderSummary = data => ({ id: data.orderId || data.id, status: data.status, paymentStatus: data.paymentStatus, fulfillmentStatus: data.fulfillmentStatus, financialPostingStatus: data.financialPostingStatus, total: data.totals?.total || data.totalAmount || data.total, rowVersion: data.rowVersion });
    try {
        await page.goto('http://localhost:5173/ecommerce/abandoned-checkouts');
        const list = await read('/admin/abandoned-checkouts?pageSize=100');
        const fixture = list.items.find(x => x.customerName?.startsWith('P4QA'));
        check('Synthetic checkout is visible in actual Admin', !!fixture);
        prefix = fixture.customerName; sessionId = fixture.checkoutSessionId;
        await page.getByRole('button', { name: 'All', exact: true }).click();
        await page.getByRole('searchbox').fill(prefix);
        await page.getByRole('button', { name: 'View', exact: true }).first().click();
        await page.getByRole('dialog').waitFor();
        const detail = await read('/admin/abandoned-checkouts/' + sessionId);
        check('Admin detail reads authoritative recoverable SQL state', ['Abandoned', 'Active'].includes(detail.status) && !!detail.recoveryUrl, { sessionId, cartId: detail.cartId });
        const accounts = await read('/admin/whatsapp/accounts');
        originalAccount = accounts[0];
        check('Messaging account initial state captured', !!originalAccount, { enabled: originalAccount.enabled, status: originalAccount.status });
        if (originalAccount.enabled) {
            const disable = await admin('post', '/admin/whatsapp/accounts/' + originalAccount.id + '/disable');
            paused = disable.status === 200;
            check('Supported account disable persisted before synthetic orders', paused && !(await read('/admin/whatsapp/accounts/' + originalAccount.id)).enabled);
        }
        storefront = await page.context().newPage();
        storefront.on('response', r => { if (/\/api\//.test(r.url())) requests.push({ path: r.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0].replace(/(recover\/).+/, '$1[redacted]'), method: r.request().method(), status: r.status() }); });
        await storefront.goto('http://ahramstore.localhost:5174/');
        await storefront.locator('main').waitFor();
        const before = await sf('get', '/storefront/cart');
        check('Recovery customer owns the intended current cart', before.status === 200 && before.data.cartId === detail.cartId && before.data.ownerType.toLowerCase() === 'customer');
        await storefront.goto('http://ahramstore.localhost:5174' + detail.recoveryUrl.replace(/^https?:\/\/[^/]+/, ''));
        await storefront.waitForURL('**/checkout', { timeout: 30000 });
        const recovered = await sf('get', '/storefront/cart');
        check('Real recovery landing reconstructs intended basket without duplicates', recovered.status === 200 && recovered.data.cartId === before.data.cartId && recovered.data.items.length === before.data.items.length && recovered.data.itemCount === before.data.itemCount);
        const repeated = await sf('get', '/storefront/checkout-session/recover/' + detail.recoveryUrl.split('/').pop());
        check('Repeated recovery is resumable, not an order conversion', repeated.status === 200 && repeated.data.cartId === detail.cartId && repeated.data.cartWasRevalidated, { status: repeated.status, total: repeated.data.cartTotal });
        const active = await read('/admin/abandoned-checkouts/' + sessionId);
        check('Recovery reactivates checkout but does not mark it converted or paid', active.status === 'Active' && !active.convertedOrderId);
        // Use actual frontend actions and its rendered checkout controls. No
        // alternate pricing or validation implementation is installed.
        const configured = await storefront.evaluate(async ({ prefix }) => {
            const url = performance.getEntriesByType('resource').find(e => /\/store\/checkoutStore\.ts(?:\?|$)/.test(e.name))?.name;
            const { useCheckoutStore } = await import(url || '/src/store/checkoutStore.ts');
            const s = useCheckoutStore.getState();
            s.setAddress({ fullName: prefix, phone: '+201000000099', city: 'QA City', area: 'QA Area', addressLine: 'Synthetic QA address', country: 'EG', notes: prefix });
            await s.fetchCheckoutFields(); await s.initCheckoutSession(); await s.fetchShippingMethods();
            const current = useCheckoutStore.getState();
            return { methods: current.shippingMethods.map(m => ({ id: m.id, name: m.name, fulfillmentMode: m.fulfillmentMode })), fields: current.checkoutFields.map(f => ({ id: f.id, kind: f.fieldKind, required: f.isRequired, key: f.systemFieldKey })) };
        }, { prefix });
        check('Actual checkout frontend loads shipping and definitions', configured.methods.length > 0, { methods: configured.methods });
        const method = configured.methods.find(m => m.fulfillmentMode === 'Manual') || configured.methods[0];
        await storefront.locator('input[name=shippingMethod][value="' + method.id + '"]').check();
        await storefront.getByRole('button', { name: /Continue to Payment/i }).click();
        await storefront.getByText(/Cash on Delivery/i).first().waitFor();
        const paymentRadio = storefront.getByRole('radio').first();
        await paymentRadio.check();
        await storefront.getByRole('button', { name: /Review Order|Continue to Review/i }).click();
        const place = storefront.getByRole('button', { name: /Place Order/i });
        await place.waitFor();
        const captured = [];
        const submitListener = r => { if (/\/api\/storefront\/checkout$/.test(r.url()) && r.request().method() === 'POST') captured.push(r); };
        storefront.on('response', submitListener);
        await place.click();
        await storefront.waitForURL(/checkout\/pending|order-success|checkout\/success|orders\//, { timeout: 45000 }).catch(() => {});
        storefront.off('response', submitListener);
        const createdList = await read('/admin/orders?search=' + prefix + '&pageSize=100');
        check('Exactly one order persisted after actual Place Order UI', createdList.items.length === 1, { count: createdList.items.length, submittedRequests: captured.length });
        const firstId = createdList.items[0].id || createdList.items[0].orderId;
        orders.push({ id: firstId, label: 'recovered-cancel' });
        let first = await read('/admin/orders/' + firstId);
        check('Committed order is pending and unpaid, not paid by recovery', first.status === 'Pending' && first.paymentStatus === 'Pending', orderSummary(first));
        const converted = await read('/admin/abandoned-checkouts/' + sessionId);
        check('Successful conversion removes abandoned classification', converted.status === 'ConvertedToOrder' && converted.convertedOrderId === firstId);
        const consumed = await sf('get', '/storefront/checkout-session/recover/' + detail.recoveryUrl.split('/').pop());
        check('Converted checkout rejects old recovery link', consumed.status === 400 && consumed.data.code === 'SESSION_CONVERTED', { status: consumed.status, code: consumed.data.code });
        const invalidComplete = await admin('post', '/orders/' + firstId + '/fulfillment/complete', {});
        check('Premature completion rejected without payment invention', invalidComplete.status >= 400, { status: invalidComplete.status });
        const invalidStatus = await admin('put', '/admin/orders/' + firstId + '/status', { newStatus: 'Completed', reason: prefix }, first.rowVersion);
        check('Direct lifecycle bypass rejected', invalidStatus.status >= 400, { status: invalidStatus.status });
        const concurrent = await Promise.all([
            admin('put', '/admin/orders/' + firstId + '/status', { newStatus: 'OnHold', reason: prefix }, first.rowVersion),
            admin('put', '/admin/orders/' + firstId + '/status', { newStatus: 'OnHold', reason: prefix }, first.rowVersion),
        ]);
        check('Competing administrators produce one admitted version and one conflict', concurrent.filter(x => x.status === 200).length === 1 && concurrent.some(x => x.status === 409), { statuses: concurrent.map(x => x.status) });
        await page.goto('http://localhost:5173/ecommerce/orders/' + firstId);
        await page.getByText(createdList.items[0].orderNumber, { exact: true }).first().waitFor();
        const cancel = await admin('post', '/admin/orders/' + firstId + '/cancel', { reason: prefix + ' supported QA cancellation' });
        check('Supported cancellation accepted', cancel.status === 200, { status: cancel.status });
        for (let i = 0; i < 20; i++) { first = await read('/admin/orders/' + firstId); if (first.status === 'Cancelled') break; await page.waitForTimeout(500); }
        check('Cancellation reaches authoritative terminal state', first.status === 'Cancelled', orderSummary(first));
        const duplicate = await admin('post', '/admin/orders/' + firstId + '/cancel', { reason: prefix + ' repeat' });
        check('Duplicate cancellation is idempotent', duplicate.status === 200 && (await read('/admin/orders/' + firstId)).status === 'Cancelled');
        // A second order uses the same real cart/pricing/warehouse selection APIs,
        // then exercises only the explicitly supported Manual COD lifecycle.
        const line = before.data.items[0];
        const added = await sf('post', '/storefront/cart/items', { productId: line.productId, variantId: line.variantId, quantity: 1 });
        check('Second synthetic cart accepted', added.status === 200);
        const session = await sf('post', '/storefront/checkout-session', { cartId: added.data.cartId, source: prefix + 'complete' });
        const selected = await sf('post', '/storefront/shipping-methods/select', { checkoutSessionId: session.data.sessionId, shippingMethodId: method.id });
        check('Server admits second order warehouse selection', selected.status === 200, { status: selected.status });
        const price = await sf('post', '/storefront/checkout/pricing', { cartId: added.data.cartId, shippingMethodId: method.id, paymentMethod: 'COD', shippingCountry: 'EG' });
        const body = { cartId: added.data.cartId, checkoutSessionId: session.data.sessionId, warehouseResolutionId: selected.data.warehouseResolutionId, warehouseResolutionVersion: selected.data.resolutionVersion, shippingMethodId: method.id, paymentMethod: 'COD', shippingAddress: { name: prefix, phone: '+201000000099', city: 'QA City', state: 'QA Area', street: 'Synthetic QA address', country: 'EG' }, customerNotes: prefix + ' complete', expectedPricing: price.data };
        const forged = await sf('post', '/storefront/checkout', { ...body, expectedPricing: { ...body.expectedPricing, total: .01 } });
        check('Forged final payable total rejected', forged.status === 409, { status: forged.status, code: forged.data.code });
        const second = await sf('post', '/storefront/checkout', body);
        check('Second authoritative checkout succeeds', second.status === 200, { status: second.status, code: second.data?.code });
        const secondId = second.data.orderId; orders.push({ id: secondId, label: 'manual-complete' });
        const replay = await sf('post', '/storefront/checkout', body);
        check('Identical checkout replays the same order', replay.status === 200 && replay.data.orderId === secondId);
        for (const action of ['confirm', 'pack', 'out-for-delivery', 'delivered']) {
            const result = await admin('post', '/orders/' + secondId + '/fulfillment/' + action, { deliveryReference: prefix, recipientName: 'Synthetic QA' });
            check('Manual fulfillment ' + action, result.status === 200, { status: result.status, code: result.data?.code, message: result.data?.message });
        }
        const unpaid = await admin('post', '/orders/' + secondId + '/fulfillment/complete', {});
        check('Delivered unpaid COD cannot complete', unpaid.status >= 400, { status: unpaid.status });
        const collection = await admin('post', '/payments/lifecycle/' + secondId + '/cod-delivered', { deliveryReference: prefix });
        check('Synthetic internal COD collection accepted without gateway', collection.status === 200, { status: collection.status });
        let final;
        for (let i = 0; i < 30; i++) { final = await read('/admin/orders/' + secondId); if (final.financialPostingStatus === 'Posted') break; await page.waitForTimeout(500); }
        check('Collection journal is posted before completion', final.financialPostingStatus === 'Posted', orderSummary(final));
        const complete = await admin('post', '/orders/' + secondId + '/fulfillment/complete', {});
        check('Financially admitted Manual completion succeeds', complete.status === 200, { status: complete.status });
        const repeat = await admin('post', '/orders/' + secondId + '/fulfillment/complete', {});
        check('Duplicate completion does not repeat transition', repeat.status === 200);
        final = await read('/admin/orders/' + secondId);
        check('Fresh completed order readback preserves distinct payment state', final.status === 'Completed' && ['Collected', 'Settled'].includes(final.paymentStatus), orderSummary(final));
        await page.goto('http://localhost:5173/ecommerce/orders/' + secondId);
        await page.getByText(final.orderNumber, { exact: true }).first().waitFor();
        await sf('delete', '/storefront/cart');
        check('Fresh customer basket cleanup readback is empty', (await sf('get', '/storefront/cart')).data.items.length === 0);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally {
        if (paused && originalAccount) {
            try {
                const current = await read('/admin/whatsapp/accounts/' + originalAccount.id);
                if (!current.enabled && current.phoneNumber === originalAccount.phoneNumber && current.useForOrderConfirmation === originalAccount.useForOrderConfirmation && current.useForOtpVerification === originalAccount.useForOtpVerification) {
                    const status = await admin('get', '/admin/whatsapp/accounts/' + originalAccount.id + '/status');
                    const restore = status.status === 200 && status.data.isConnected ? await admin('post', '/admin/whatsapp/accounts/' + originalAccount.id + '/enable') : { status: 0 };
                    check('Original account sending gate restored after scoped QA', restore.status === 200 && (await read('/admin/whatsapp/accounts/' + originalAccount.id)).enabled);
                } else failures.push({ message: 'Account changed concurrently; restoration not forced' });
            } catch { failures.push({ message: 'Account restoration requires review' }); }
        }
        if (storefront) await storefront.close();
    }
    return { prefix, sessionId, orders, checks, failures, requests };
}

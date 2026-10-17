async page => {
    const checks = [], failures = [], retained = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const prefix = 'P4QA1792177832429', customer = '3e6129bf-5bac-4d26-86da-c7c42cb53833';
    let storefront;
    const api = (method, path, data) => page.evaluate(async ({ method, path, data }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data });
    try {
        await page.goto('http://localhost:5173/ecommerce/orders'); await page.locator('main h1').waitFor();
        const list = await api('get', '/admin/orders?search=' + prefix + '&pageSize=50');
        check('Fresh synthetic order list has exactly two retained lifecycle records', list.status === 200 && list.data.items.length === 2, { orders: list.data.items.map(x => ({ id: x.orderId, status: x.status, paymentStatus: x.paymentStatus, total: x.totalAmount })) });
        for (const state of ['Cancelled', 'Completed']) {
            const filtered = await api('get', '/admin/orders?search=' + prefix + '&status=' + state + '&pageSize=1');
            check('Actual order filter/pagination: ' + state, filtered.status === 200 && filtered.data.items.length === 1 && filtered.data.items[0].status === state);
        }
        check('No synthetic phone remains blocked', (await api('get', '/admin/phones?search=P4QA')).data.totalCount === 0);
        const templates = await api('get', '/admin/whatsapp/templates');
        check('No synthetic WhatsApp template override remains', templates.status === 200 && !templates.data.some(x => x.content?.startsWith('P4QA')));
        const config = await api('get', '/admin/ecommerce/abandoned-cart-recovery/settings');
        check('Recovery automation remains disabled after settings restoration', config.status === 200 && !config.data.enableAbandonedRecovery, { firstMessageDelayMinutes: config.data.firstMessageDelayMinutes, secondMessageDelayMinutes: config.data.secondMessageDelayMinutes, timeZoneId: config.data.timeZoneId });
        const account = await api('get', '/admin/whatsapp/accounts/138379f5-7754-4c20-8863-b3b7aad28888');
        const transport = await api('get', '/admin/whatsapp/accounts/138379f5-7754-4c20-8863-b3b7aad28888/status');
        retained.push({ type: 'WhatsApp account QA sending gate', id: '138379f5-7754-4c20-8863-b3b7aad28888', enabled: account.data.enabled, status: account.data.status, originalEnabled: true, originalStatus: 'Connected', restorationBlocked: transport.status !== 200, providerStatus: transport.status, providerCode: transport.data.code });
        const customers = await api('get', '/admin/online-customers?pageSize=100');
        const detail = await api('get', '/admin/online-customers/' + customer);
        const item = customers.data.items.find(x => x.id === customer);
        check('Registered account list/detail agree on current active refresh sessions', customers.status === 200 && detail.status === 200 && item?.activeSessions === detail.data.activeSessions, { activeSessions: detail.data.activeSessions, registeredAccounts: customers.data.totalCount, heartbeatPresence: false });
        storefront = await page.context().newPage();
        await storefront.goto('http://ahramstore.localhost:5174/'); await storefront.locator('main').waitFor();
        const cart = await storefront.evaluate(async () => {
            const { httpClient } = await import('/src/services/api/httpClient.ts');
            const before = (await httpClient.get('/api/storefront/cart')).data;
            if (before.ownerType.toLowerCase() !== 'customer') throw new Error('No customer cart authority');
            if (before.items.length > 0) {
                if (before.items.length !== 1 || before.items[0].productId !== '03144bba-1880-c1d6-3062-997a1006feca' || before.items[0].quantity !== 1) throw new Error('Unexpected cart content; cleanup not applied');
                await httpClient.delete('/api/storefront/cart');
            }
            const after = (await httpClient.get('/api/storefront/cart')).data;
            return { ownerType: after.ownerType, cartId: after.cartId, beforeItems: before.items.length, items: after.items.length, subtotal: after.subtotal };
        });
        check('Supported synthetic basket cleanup has fresh customer readback', cart.ownerType.toLowerCase() === 'customer' && cart.items === 0 && cart.subtotal === 0, cart);
        const converted = await api('get', '/admin/abandoned-checkouts/1efa806b-9dd3-4118-a40e-df51b2e6f025');
        const consumed = await storefront.evaluate(async url => {
            const { httpClient } = await import('/src/services/api/httpClient.ts');
            try { return { status: (await httpClient.get('/api/storefront/checkout-session/recover/' + url.split('/').pop())).status }; }
            catch (e) { return { status: e.response?.status, code: e.response?.data?.code }; }
        }, converted.data.recoveryUrl);
        check('Converted checkout cannot reuse historical recovery link', consumed.status === 400 && consumed.code === 'SESSION_CONVERTED', consumed);
    } catch (e) { failures.push(e.message.split('\n')[0]); }
    finally { if (storefront) await storefront.close(); }
    return { observedUtc: new Date().toISOString(), checks, failures, retained };
}

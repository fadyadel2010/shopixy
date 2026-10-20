// Final hosted observer. No settings/cart/order/payment mutation or inference.
async page => {
    const checks = [], failures = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const diagnostics = [];
    const onConsole = message => { if (message.text().startsWith('[HTTP]')) diagnostics.push(message.text()); };
    page.on('console', onConsole);
    const credential = 'P8QA_DIAGNOSTIC_NOT_A_CREDENTIAL';
    try {
        await page.goto('http://ahramstore.localhost:5174/', { waitUntil: 'domcontentloaded' });
        await page.locator('main').waitFor();
        const disabled = await page.evaluate(async () => (await fetch('/api/storefront/store-config', { cache: 'no-store' }).then(r => r.json())).commerceAiEnabled === false);
        if (!disabled) throw new Error('AI enabled; keeping transport block and refusing admission probe');
        // These exact guards were installed by this task, before authoring.
        // Release them only after fresh disabled authority, to exercise403.
        await page.unroute('**/api/storefront/chat**');
        await page.context().unroute('**/api/storefront/chat**');
        const current = await page.evaluate(async credential => {
            const { httpClient: h } = await import('/src/services/api/httpClient.ts');
            const me = await h.get('/api/storefront/auth/me');
            const cart = await h.get('/api/storefront/cart');
            const order = await h.get('/api/storefront/orders/70461d1c-51a1-4161-987a-eae2d8cbd668');
            const config = await h.get('/api/storefront/store-config');
            if (config.data.commerceAiEnabled !== false) throw new Error('AI is not disabled; refusing any chat admission probe');
            let disabledChat;
            try { await h.post('/api/storefront/chat', { message: 'P8QA disabled admission negative; no inference authorized' }); }
            catch (e) { disabledChat = { status: e.response?.status, cache: e.response?.headers?.['cache-control'], hasEtag: !!e.response?.headers?.etag }; }
            let invalid;
            try { await h.get('/api/storefront/checkout-session/recover/' + credential); }
            catch (e) { invalid = { status: e.response?.status, code: e.response?.data?.code }; }
            return { meStatus: me.status, customerId: me.data.id, cart: { owner: cart.data.ownerType, count: cart.data.items.length, subtotal: cart.data.subtotal },
                order: { id: order.data.orderId || order.data.id, status: order.data.status, paymentStatus: order.data.paymentStatus, total: order.data.total || order.data.totalAmount, currency: order.data.currency },
                config: { aiEnabled: config.data.commerceAiEnabled, tenantId: config.data.tenantId }, invalid, disabledChat };
        }, credential);
        check('Current password session has server-authoritative QA identity', current.meStatus === 200 && current.customerId === '3e6129bf-5bac-4d26-86da-c7c42cb53833');
        check('Fresh current customer-owned cart remains empty', current.cart.owner.toLowerCase() === 'customer' && current.cart.count === 0 && current.cart.subtotal === 0);
        check('Customer can read exactly the retained own order', current.order.id === '70461d1c-51a1-4161-987a-eae2d8cbd668', { order: current.order });
        check('Public merchant AI remains disabled', current.config.aiEnabled === false);
        check('Actual disabled chat admission returns403 with central private/no-store headers', current.disabledChat?.status === 403 && /private/.test(current.disabledChat.cache) && /no-store/.test(current.disabledChat.cache) && !current.disabledChat.hasEtag, { disabledChat: current.disabledChat });
        check('Unresolvable synthetic recovery token is rejected by actual server', current.invalid?.status === 404 && current.invalid.code === 'SESSION_NOT_FOUND', { rejection: current.invalid });
        check('Actual HTTP diagnostics omit protected URL credential material', diagnostics.length > 0 && diagnostics.every(text => !text.includes(credential)), { diagnosticEntriesObserved: diagnostics.length });
        const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
        const converted = await admin.evaluate(async () => {
            const api = (await import('/src/services/api.js')).default;
            const r = await api.get('/admin/abandoned-checkouts/73ad926e-dcb5-4ee4-91af-77e709577f46');
            return { status: r.data.status, orderId: r.data.convertedOrderId, url: r.data.recoveryUrl };
        });
        check('Admin SQL checkout classification is converted to exact order', converted.status === 'ConvertedToOrder' && converted.orderId === '70461d1c-51a1-4161-987a-eae2d8cbd668');
        if (converted.url) {
            const rejection = await page.evaluate(async url => {
                const { httpClient } = await import('/src/services/api/httpClient.ts');
                try { await httpClient.get('/api/storefront/checkout-session/recover/' + url.split('/').pop()); return { status: 200 }; }
                catch (e) { return { status: e.response?.status, code: e.response?.data?.code }; }
            }, converted.url);
            check('Converted recovery link cannot restore or convert again', rejection.status === 400 && rejection.code === 'SESSION_CONVERTED', rejection);
        }
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally { page.off('console', onConsole); }
    return { observedUtc: new Date().toISOString(), checks, failures, modelRequests: 0, protectedTokensPrinted: false };
}

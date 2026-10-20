// Resume exact manifested checkout. Every checkout response is observed before
// any next submission. Unknown/timeout/committed negative outcomes stop the run.
async page => {
    const scenario = await page.evaluate(() => JSON.parse(sessionStorage.getItem('phase8-scenario')));
    const checks = [], failures = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const persist = () => page.evaluate(s => sessionStorage.setItem('phase8-scenario', JSON.stringify(s)), scenario);
    const adminPage = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const admin = (method, path, data, version) => adminPage.evaluate(async ({ method, path, data, version }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data, headers: version ? { 'If-Match': version } : {} }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data, version });
    const sf = (method, path, data, headers = {}) => page.evaluate(async ({ method, path, data, headers }) => {
        if (headers['X-Tenant-Id']) {
            // The shared client deliberately replaces tenant headers. Use the
            // raw transport for this negative, preserving CSRF/current identity.
            const { getRequestHeaders } = await import('/src/bootstrap/index.ts');
            const { appendCsrfHeader } = await import('/src/services/api/csrf.ts');
            const actual = appendCsrfHeader({ ...getRequestHeaders(), 'Content-Type': 'application/json', ...headers }, method);
            const r = await fetch('/api/storefront' + path, { method, headers: actual, credentials: 'include', body: JSON.stringify(data) });
            return { status: r.status, data: await r.json() };
        }
        const { httpClient } = await import('/src/services/api/httpClient.ts');
        try { const r = await httpClient.request({ method, url: '/api/storefront' + path, data, headers }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data, headers });
    let disabled = false;
    try {
        check('Recorded scope has no earlier submission attempt', scenario.marker === 'P8QA20261018-1116' && !!scenario.body && !scenario.submissionAttempted);
        const before = await admin('get', '/admin/abandoned-checkouts/' + scenario.sessionId);
        check('Exact abandoned session exposes supported recovery path', before.status === 200 && before.data.status === 'Abandoned' && before.data.cartId === scenario.cartId && !!before.data.recoveryUrl);
        const token = before.data.recoveryUrl.split('/').pop();
        await page.goto('http://ahramstore.localhost:5174' + before.data.recoveryUrl.replace(/^https?:\/\/[^/]+/, ''), { waitUntil: 'domcontentloaded' });
        await page.waitForURL('**/checkout', { timeout: 45000 });
        const recovered = await sf('get', '/cart');
        check('Real recovery route restores same customer cart without duplicate lines', recovered.status === 200 && recovered.data.cartId === scenario.cartId && recovered.data.items.length === 1 && recovered.data.totalDiscount === 5);
        const replayRecovery = await sf('get', '/checkout-session/recover/' + token);
        check('Preconversion replay revalidates instead of converting', replayRecovery.status === 200 && replayRecovery.data.cartWasRevalidated && replayRecovery.data.cartId === scenario.cartId);
        const active = await admin('get', '/admin/abandoned-checkouts/' + scenario.sessionId);
        check('Recovered session is Active and not converted/paid', active.status === 200 && active.data.status === 'Active' && !active.data.convertedOrderId);
        const fields = await sf('get', '/checkout-fields');
        check('Actual checkout consumer receives supported public definitions', fields.status === 200 && Array.isArray(fields.data.fields) && fields.data.fields.length > 0, { count: fields.data?.fields?.length });
        const selection = await sf('post', '/shipping-methods/select', { checkoutSessionId: scenario.sessionId, shippingMethodId: scenario.methodId });
        check('Recovered shipping is freshly reselected server-side', selection.status === 200);
        scenario.body.warehouseResolutionId = selection.data.warehouseResolutionId; scenario.body.warehouseResolutionVersion = selection.data.resolutionVersion;
        scenario.submissionAttempted = true; await persist();
        const foreign = await sf('post', '/checkout', scenario.body, { 'X-Tenant-Id': '11111111-1111-4111-8111-111111111111' });
        if (foreign.data?.orderId) scenario.orderId = foreign.data.orderId;
        await persist();
        check('Foreign tenant cannot submit this owned cart', foreign.status === 403, { status: foreign.status, code: foreign.data?.code });
        const forged = await sf('post', '/checkout', { ...scenario.body, expectedPricing: { ...scenario.expectedPricing, subtotal: 100, total: 105 } });
        if (forged.data?.orderId) scenario.orderId = forged.data.orderId;
        await persist();
        check('Internally consistent forged canonical subtotal/total rejected', [400, 409].includes(forged.status), { status: forged.status, code: forged.data?.code });
        let coupon = await admin('get', '/coupons/' + scenario.couponId);
        const off = await admin('patch', '/coupons/' + scenario.couponId + '/toggle', null, coupon.data.rowVersion);
        check('Only owned coupon disabled after accepted preview', off.status === 200); disabled = true;
        const stale = await sf('post', '/checkout', scenario.body);
        if (stale.data?.orderId) scenario.orderId = stale.data.orderId;
        await persist();
        check('Disabled offer cannot reuse previously accepted price at submission', [400, 409].includes(stale.status), { status: stale.status, code: stale.data?.code });
        const absent = await admin('get', '/admin/orders?search=' + scenario.marker + '&pageSize=50');
        check('Fresh observer confirms rejected submissions created no order', absent.status === 200 && absent.data.items.length === 0);
        coupon = await admin('get', '/coupons/' + scenario.couponId);
        check('Owned coupon reenabled with current revision', (await admin('patch', '/coupons/' + scenario.couponId + '/toggle', null, coupon.data.rowVersion)).status === 200); disabled = false;
        const applied = await sf('post', '/cart/apply-coupon', { code: scenario.marker });
        check('Recovered coupon state is revalidated again', applied.status === 200 && applied.data.totalDiscount === 5);
        const price = await sf('post', '/checkout/pricing', { cartId: scenario.cartId, shippingMethodId: scenario.methodId, paymentMethod: 'COD', shippingCountry: 'EG', couponCode: scenario.marker });
        check('Fresh final price still matches canonical30', price.status === 200 && price.data.total === 30);
        const created = await sf('post', '/checkout', scenario.body);
        scenario.orderId = created.data?.orderId; await persist();
        check('One valid canonical checkout returns persisted order identity', created.status === 200 && !!scenario.orderId, { status: created.status, orderId: scenario.orderId, total: created.data?.totalAmount, currency: created.data?.currency });
        const orders = await admin('get', '/admin/orders?search=' + scenario.marker + '&pageSize=50');
        check('Independent fresh Admin lookup finds exactly this order', orders.status === 200 && orders.data.items.length === 1 && (orders.data.items[0].id || orders.data.items[0].orderId) === scenario.orderId);
        const order = await admin('get', '/admin/orders/' + scenario.orderId);
        check('Persisted order accepted EGP30, shipping10, Pending/unpaid', order.status === 200 && order.data.totals.total === 30 && order.data.totals.shippingCost === 10 && order.data.paymentStatus === 'Pending' && order.data.status === 'Pending');
        const duplicate = await sf('post', '/checkout', scenario.body);
        check('Exact committed checkout retry returns same order', duplicate.status === 200 && duplicate.data.orderId === scenario.orderId, { status: duplicate.status, orderId: duplicate.data?.orderId });
        const consumed = await sf('get', '/checkout-session/recover/' + token);
        check('Converted recovery token is rejected', consumed.status === 400 && consumed.data.code === 'SESSION_CONVERTED');
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally {
        if (disabled) {
            const current = await admin('get', '/coupons/' + scenario.couponId);
            if (current.status === 200 && current.data.code === scenario.marker && !current.data.isActive)
                await admin('patch', '/coupons/' + scenario.couponId + '/toggle', null, current.data.rowVersion);
        }
        await persist();
    }
    return { observedUtc: new Date().toISOString(), marker: scenario.marker, orderId: scenario.orderId, couponId: scenario.couponId, cartId: scenario.cartId, sessionId: scenario.sessionId, methodId: scenario.methodId, expectedPricing: scenario.expectedPricing, checks, failures };
}

// Creates at most one new COD order. Resume a recorded ID after any post-commit
// failure; never rerun this checkout harness to recover a lost response.
async page => {
    const prefix = 'P5QA20261017-817';
    const checks = [], failures = [], requests = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const storefront = await page.context().newPage();
    const admin = (method, path, data, revision) => page.evaluate(async ({ method, path, data, revision }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data, headers: revision ? { 'If-Match': revision } : {} }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code || e.response?.data?.error, message: e.response?.data?.message } }; }
    }, { method, path, data, revision });
    const read = async path => { const r = await admin('get', path); if (r.status !== 200) throw new Error('Read ' + path + ' ' + r.status); return r.data; };
    const sf = (method, path, data) => storefront.evaluate(async ({ method, path, data }) => {
        const { httpClient } = await import('/src/services/api/httpClient.ts');
        try { const r = await httpClient.request({ method, url: '/api' + path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data });
    let orderId, shipmentId, cartId, method, submitted = false;
    storefront.on('response', r => { if (r.url().includes('/api/')) requests.push({ path: r.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0], status: r.status(), method: r.request().method() }); });
    try {
        const existing = await read('/admin/orders?search=' + prefix + '&pageSize=50');
        check('No existing COD scenario before checkout', !existing.items.some(o => o.paymentMethodCode === 'COD' && o.customerName?.startsWith(prefix)));
        method = (await read('/shippingmethods?activeOnly=false')).find(m => m.name === prefix + ' Delivery');
        check('Task-owned shipping method exists with no prior order submission', !!method);
        if (!method.isActive) {
            check('Reactivate exact task-owned fixture before new scenario', (await admin('put', '/shippingmethods/' + method.id, { ...method, isActive: true }, method.revision)).status === 204);
            method = await read('/shippingmethods/' + method.id);
        }
        const accounts = await read('/admin/whatsapp/accounts');
        check('QA-disabled outbound messaging stays disabled', accounts.every(a => !a.enabled));
        await storefront.goto('http://ahramstore.localhost:5174/cart'); await storefront.locator('main').waitFor();
        const before = await sf('get', '/storefront/cart');
        check('Customer cart starts empty', before.status === 200 && before.data.ownerType.toLowerCase() === 'customer' && before.data.items.length === 0);
        const products = await sf('get', '/storefront/products/discover?pageSize=20');
        let added;
        for (const p of products.data.items) {
            added = await sf('post', '/storefront/cart/items', { productId: p.id || p.productId, quantity: 1 });
            if (added.status === 200) break;
        }
        check('Synthetic COD cart line accepted', added.status === 200); cartId = added.data.cartId;
        const session = await sf('post', '/storefront/checkout-session', { cartId, source: prefix + '-COD' });
        check('New checkout session accepted', session.status === 200);
        const sessionId = session.data.sessionId;
        await sf('patch', '/storefront/checkout-session/' + sessionId, { customerName: prefix + ' COD', phone: '+201000000099', city: 'QA City', area: 'QA Area', address: 'Synthetic QA address - no delivery', country: 'EG', notes: prefix });
        const selected = await sf('post', '/storefront/shipping-methods/select', { checkoutSessionId: sessionId, shippingMethodId: method.id });
        check('Authoritative warehouse selection accepted', selected.status === 200);
        const country = await sf('post', '/storefront/checkout/pricing', { cartId, shippingMethodId: method.id, paymentMethod: 'COD', shippingCountry: 'US' });
        check('Out-of-country method rejected by pricing authority', country.status >= 400 && country.status < 500, { status: country.status, code: country.data.code });
        const disabled = await admin('put', '/shippingmethods/' + method.id, { ...method, isActive: false }, method.revision);
        check('Synthetic method deactivation persisted', disabled.status === 204);
        const stale = await sf('post', '/storefront/checkout/pricing', { cartId, shippingMethodId: method.id, paymentMethod: 'COD', shippingCountry: 'EG' });
        check('Previously selected but now disabled shipping denied server-side', stale.status >= 400 && stale.status < 500, { status: stale.status, code: stale.data.code });
        const fresh = await read('/shippingmethods/' + method.id);
        check('Stale shipping editor version refused', (await admin('put', '/shippingmethods/' + method.id, method, method.revision)).status === 409);
        check('Task-owned method reactivated with fresh revision', (await admin('put', '/shippingmethods/' + method.id, { ...fresh, isActive: true }, fresh.revision)).status === 204);
        const selection = await sf('post', '/storefront/shipping-methods/select', { checkoutSessionId: sessionId, shippingMethodId: method.id });
        const price = await sf('post', '/storefront/checkout/pricing', { cartId, shippingMethodId: method.id, paymentMethod: 'COD', shippingCountry: 'EG' });
        check('Actual authoritative quote recalculated', price.status === 200, { pricing: price.data });
        const expectedPricing = Object.fromEntries(['subtotal', 'shipping', 'tax', 'discount', 'total', 'couponDiscount', 'promotionDiscount', 'cartPromotionDiscount', 'paymentFee', 'manualDiscount'].map(k => [k, price.data[k] ?? 0]));
        const body = { cartId, checkoutSessionId: sessionId, warehouseResolutionId: selection.data.warehouseResolutionId, warehouseResolutionVersion: selection.data.resolutionVersion, shippingMethodId: method.id, paymentMethod: 'COD', shippingAddress: { name: prefix + ' COD', phone: '+201000000099', city: 'QA City', state: 'QA Area', street: 'Synthetic QA address - no delivery', country: 'EG' }, customerNotes: prefix + ' COD synthetic internal collection - no physical cash', expectedPricing };
        submitted = true;
        const created = await sf('post', '/storefront/checkout', body); orderId = created.data.orderId;
        check('New canonical-priced COD checkout committed once', created.status === 200 && !!orderId, { status: created.status, orderId, total: created.data.totalAmount, currency: created.data.currency });
        const beforeOrder = await read('/admin/orders/' + orderId);
        check('Saved shipping/payment choices agree with quote', beforeOrder.totals.total === price.data.total && beforeOrder.totals.shippingCost === price.data.shipping && beforeOrder.paymentStatus === 'Pending');
        check('Confirm admitted', (await admin('post', '/orders/' + orderId + '/fulfillment/confirm', {})).status === 200);
        let order;
        for (let i = 0; i < 80; i++) { order = await read('/admin/orders/' + orderId); shipmentId = order.shipment?.shipmentId; if (shipmentId) break; await page.waitForTimeout(500); }
        check('Original local Manual shipment created by actual worker', !!shipmentId, { shipmentId });
        const duplicate = await Promise.all([admin('post', '/shipments', { orderId, shippingMethodId: method.id }), admin('post', '/shipments', { orderId, shippingMethodId: method.id })]);
        check('Concurrent repeated local creation does not create another shipment', duplicate.every(r => r.status === 409), { statuses: duplicate.map(r => r.status) });
        const currentMethod = await read('/shippingmethods/' + method.id);
        check('Task-owned method price edit accepted after order capture', (await admin('put', '/shippingmethods/' + method.id, { ...currentMethod, baseRate: 99 }, currentMethod.revision)).status === 204);
        check('Existing order quote preserved after method edit', (await read('/admin/orders/' + orderId)).totals.shippingCost === price.data.shipping);
        check('Pack admitted', (await admin('post', '/orders/' + orderId + '/fulfillment/pack', {})).status === 200);
        const reference = prefix + '-LOCAL';
        for (const status of ['ReadyForPickup', 'PickedUp', 'InTransit', 'OutForDelivery', 'Delivered']) {
            const r = await admin('put', '/admin/shipments/' + shipmentId + '/status', { newStatus: status, trackingNumber: reference, reason: prefix + ' synthetic lifecycle only', deliveryReference: reference, proofOfDeliveryUrl: 'https://example.test/p5qa/' + orderId });
            check('Actual Manual shipment transition ' + status, r.status === 200, { status: r.status, code: r.data?.code, message: r.data?.message });
        }
        const shipment = await read('/admin/shipments/' + shipmentId);
        check('Fresh shipment readback delivered but not collected', shipment.status === 'Delivered' && !shipment.codCollected && shipment.trackingNumber === reference, { shipmentId, shipmentStatus: shipment.status });
        check('Unpaid completion denied', (await admin('post', '/orders/' + orderId + '/fulfillment/complete', {})).status === 409);
        const collection = await Promise.all([admin('post', '/payments/lifecycle/' + orderId + '/cod-delivered', { deliveryReference: reference }), admin('post', '/payments/lifecycle/' + orderId + '/cod-delivered', { deliveryReference: reference })]);
        check('Competing synthetic internal COD collection is admitted or safely conflicted', collection.some(r => r.status === 200) && collection.every(r => [200, 409].includes(r.status)), { statuses: collection.map(r => r.status) });
        for (let i = 0; i < 100; i++) { order = await read('/admin/orders/' + orderId); if (order.financialPostingStatus === 'Posted') break; await page.waitForTimeout(500); }
        check('Internal collection reaches authoritative Posted accounting', order.financialPostingStatus === 'Posted' && order.paymentStatus === 'Collected', { total: order.totals.total, paymentStatus: order.paymentStatus, financialPostingStatus: order.financialPostingStatus });
        check('Repeated internal collection succeeds idempotently', (await admin('post', '/payments/lifecycle/' + orderId + '/cod-delivered', { deliveryReference: reference })).status === 200);
        const settlementId = 'a640b716-b5ef-4eba-a7ce-9ff4b498cdb4';
        const settlement = await admin('post', '/payments/cod/manual-settlements', { settlementId, orderIds: [orderId] });
        check('Synthetic internal Manual COD settlement posts to authoritative Cashbox', settlement.status === 200 && settlement.data.accountingOutcome === 'Posted', { status: settlement.status, code: settlement.data?.code, settlementId, journalId: settlement.data?.journalId, grossAmount: settlement.data?.grossAmount });
        const replay = await admin('post', '/payments/cod/manual-settlements', { settlementId, orderIds: [orderId] });
        check('Same settlement identity returns original journal without another movement', replay.status === 200 && replay.data.journalId === settlement.data.journalId && replay.data.wasIdempotent === true);
        check('Paid/posted Manual order completion admitted', (await admin('post', '/orders/' + orderId + '/fulfillment/complete', {})).status === 200);
        await page.goto('http://localhost:5173/admin/shipping/shipments/' + shipmentId);
        await page.getByText(reference, { exact: true }).first().waitFor();
        check('Actual shipment details UI displays authoritative local tracking', true);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally {
        if (cartId) { const current = await sf('get', '/storefront/cart'); if (current.status === 200 && current.data.cartId === cartId) { await sf('delete', '/storefront/cart'); checks.push({ name: 'Customer basket fresh cleanup', passed: (await sf('get', '/storefront/cart')).data.items.length === 0 }); } }
        if (method) {
            const current = await admin('get', '/shippingmethods/' + method.id);
            if (current.status === 200 && current.data.name === method.name) await admin('put', '/shippingmethods/' + method.id, { ...current.data, isActive: false }, current.data.revision);
        }
        await storefront.close();
    }
    return { prefix, orderId, shipmentId, cartId, submitted, checks, failures, requests };
}

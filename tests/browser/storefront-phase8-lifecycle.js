// Exact-order continuation only. Never submits a new checkout. Internal Manual
// fulfillment and synthetic COD accounting are not physical cash/carrier proof.
async page => {
    const s = await page.evaluate(() => JSON.parse(sessionStorage.getItem('phase8-scenario')));
    const checks = [], failures = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const adminPage = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const admin = (method, path, data, version) => adminPage.evaluate(async ({ method, path, data, version }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data, headers: version ? { 'If-Match': version } : {} }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data, version });
    const read = async path => { const r = await admin('get', path); if (r.status !== 200) throw new Error('Read ' + path + ' ' + r.status); return r.data; };
    const persist = () => page.evaluate(s => sessionStorage.setItem('phase8-scenario', JSON.stringify(s)), s);
    const settlementId = '28e64df8-aa83-46a7-a402-33d3dac50b16';
    try {
        check('Resume only independently observed exact committed order', s.orderId === '70461d1c-51a1-4161-987a-eae2d8cbd668' && s.marker === 'P8QA20261018-1116');
        const scope = await read('/admin/orders?search=' + s.marker + '&pageSize=50');
        check('One canonical-priced QA order exists', scope.items.length === 1 && (scope.items[0].id || scope.items[0].orderId) === s.orderId);
        let order = await read('/admin/orders/' + s.orderId);
        check('Saved price and quote are EGP30 and10', order.totals.total === 30 && order.totals.shippingCost === 10 && order.status === 'Pending' && order.paymentStatus === 'Pending');
        const spoof = await page.evaluate(async () => {
            const s = JSON.parse(sessionStorage.getItem('phase8-scenario'));
            const { getRequestHeaders } = await import('/src/bootstrap/index.ts');
            const { appendCsrfHeader } = await import('/src/services/api/csrf.ts');
            const headers = appendCsrfHeader({ ...getRequestHeaders(), 'Content-Type': 'application/json', 'X-Tenant-Id': '11111111-1111-4111-8111-111111111111' }, 'POST');
            const r = await fetch('/api/storefront/checkout', { method: 'POST', headers, body: JSON.stringify(s.body), credentials: 'include' });
            const b = await r.json(); return { status: r.status, code: b.code };
        });
        check('Raw checkout foreign-tenant header actually reaches middleware and is denied', spoof.status === 403, spoof);
        const replay = await page.evaluate(async () => {
            const s = JSON.parse(sessionStorage.getItem('phase8-scenario'));
            const { httpClient } = await import('/src/services/api/httpClient.ts');
            try { const r = await httpClient.post('/api/storefront/checkout', s.body); return { status: r.status, id: r.data.orderId }; }
            catch (e) { return { status: e.response?.status || 0 }; }
        });
        check('Readback-established committed checkout retries same order', replay.status === 200 && replay.id === s.orderId, replay);
        const premature = await admin('post', '/orders/' + s.orderId + '/fulfillment/complete', {});
        check('Pending unpaid order cannot complete', premature.status === 409, { status: premature.status });
        const bypass = await admin('put', '/admin/orders/' + s.orderId + '/status', { newStatus: 'Completed', reason: s.marker }, order.rowVersion);
        check('Direct status cannot bypass commercial lifecycle', [400, 409].includes(bypass.status), { status: bypass.status });
        check('Manual confirmation accepted', (await admin('post', '/orders/' + s.orderId + '/fulfillment/confirm', {})).status === 200);
        for (let i = 0; i < 100; i++) { order = await read('/admin/orders/' + s.orderId); s.shipmentId = order.shipment?.shipmentId; if (s.shipmentId) break; await page.waitForTimeout(500); }
        await persist();
        check('Actual outbox worker creates one local shipment', !!s.shipmentId, { shipmentId: s.shipmentId });
        const duplicate = await Promise.all([admin('post', '/shipments', { orderId: s.orderId, shippingMethodId: s.methodId }), admin('post', '/shipments', { orderId: s.orderId, shippingMethodId: s.methodId })]);
        check('Concurrent create replay does not duplicate shipment', duplicate.every(x => x.status === 409), { statuses: duplicate.map(x => x.status) });
        check('Pack accepts local shipment authority', (await admin('post', '/orders/' + s.orderId + '/fulfillment/pack', {})).status === 200);
        const tracking = s.marker + '-LOCAL';
        for (const status of ['ReadyForPickup', 'PickedUp', 'InTransit', 'OutForDelivery', 'Delivered']) {
            const result = await admin('put', '/admin/shipments/' + s.shipmentId + '/status', { newStatus: status, trackingNumber: tracking, reason: s.marker + ' internal synthetic progression', deliveryReference: tracking, proofOfDeliveryUrl: 'https://example.test/p8qa/' + s.orderId });
            check('Local Manual fulfillment ' + status, result.status === 200, { status: result.status, code: result.data?.code });
        }
        check('Delivered but unpaid order still cannot complete', (await admin('post', '/orders/' + s.orderId + '/fulfillment/complete', {})).status === 409);
        check('Synthetic internal COD collection accepted', (await admin('post', '/payments/lifecycle/' + s.orderId + '/cod-delivered', { deliveryReference: tracking })).status === 200);
        let financial;
        for (let i = 0; i < 100; i++) { financial = (await read('/payments/cod/manual-settlements/eligible-orders')).orders.find(x => x.orderId === s.orderId); if (financial?.collectionStatus === 'Posted') break; await page.waitForTimeout(500); }
        order = await read('/admin/orders/' + s.orderId);
        check('Accounting worker posts separate Collected/Posted authority', order.paymentStatus === 'Collected' && financial?.collectionStatus === 'Posted' && financial.eligible);
        const collections = await Promise.all([admin('post', '/payments/lifecycle/' + s.orderId + '/cod-delivered', { deliveryReference: tracking }), admin('post', '/payments/lifecycle/' + s.orderId + '/cod-delivered', { deliveryReference: tracking })]);
        check('Committed collection replay succeeds under concurrency', collections.every(x => x.status === 200));
        const settlement = await admin('post', '/payments/cod/manual-settlements', { settlementId, orderIds: [s.orderId] });
        check('Internal Manual settlement posts exact gross30', settlement.status === 200 && settlement.data.accountingOutcome === 'Posted' && settlement.data.grossAmount === 30, { settlementId, journalId: settlement.data?.journalId, status: settlement.status });
        s.settlementId = settlementId; s.settlementJournalId = settlement.data.journalId; await persist();
        const replays = await Promise.all([admin('post', '/payments/cod/manual-settlements', { settlementId, orderIds: [s.orderId] }), admin('post', '/payments/cod/manual-settlements', { settlementId, orderIds: [s.orderId] })]);
        check('Concurrent same-operation settlement returns same journal', replays.every(x => x.status === 200 && x.data.journalId === s.settlementJournalId && x.data.wasIdempotent));
        check('Posted settled order can complete', (await admin('post', '/orders/' + s.orderId + '/fulfillment/complete', {})).status === 200);
        check('Repeated completion is idempotent', (await admin('post', '/orders/' + s.orderId + '/fulfillment/complete', {})).status === 200);
        check('Completed posted order cannot be cancelled', (await admin('post', '/admin/orders/' + s.orderId + '/cancel', { reason: s.marker + ' terminal negative' })).status === 409);
        order = await read('/admin/orders/' + s.orderId);
        const posted = await read('/payments/cod/manual-settlements/' + settlementId);
        check('Fresh final order is Completed/Settled/Posted at30', order.status === 'Completed' && order.paymentStatus === 'Settled' && posted.accountingOutcome === 'Posted' && order.totals.total === 30);
        await adminPage.goto('http://localhost:5173/admin/shipping/shipments/' + s.shipmentId, { waitUntil: 'domcontentloaded' });
        await adminPage.getByText(tracking, { exact: true }).first().waitFor();
        check('Actual shipment UI displays persisted local tracking', true);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally { await persist(); }
    return { observedUtc: new Date().toISOString(), marker: s.marker, orderId: s.orderId, shipmentId: s.shipmentId, couponId: s.couponId, sessionId: s.sessionId, cartId: s.cartId, settlementId: s.settlementId, settlementJournalId: s.settlementJournalId, checks, failures };
}

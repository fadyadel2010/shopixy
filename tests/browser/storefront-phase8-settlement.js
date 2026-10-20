// Continue only the already Collected and independently Posted QA order.
async page => {
    const s = await page.evaluate(() => JSON.parse(sessionStorage.getItem('phase8-scenario')));
    const checks = [], failures = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const p = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const api = (method, path, data) => p.evaluate(async ({ method, path, data }) => {
        const a = (await import('/src/services/api.js')).default;
        try { const r = await a.request({ method, url: path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, code: e.response?.data?.code }; }
    }, { method, path, data });
    const settlementId = '28e64df8-aa83-46a7-a402-33d3dac50b16';
    try {
        check('Exact previously observed order only', s.orderId === '70461d1c-51a1-4161-987a-eae2d8cbd668' && !!s.shipmentId);
        const eligible = await api('get', '/payments/cod/manual-settlements/eligible-orders');
        const selected = eligible.data.orders.find(x => x.orderId === s.orderId);
        check('Financial authority proves Posted collection at EGP30', eligible.status === 200 && selected?.eligible && selected.collectionStatus === 'Posted' && selected.amount === 30);
        const replayCollection = await Promise.all([api('post', '/payments/lifecycle/' + s.orderId + '/cod-delivered', { deliveryReference: s.marker + '-LOCAL' }), api('post', '/payments/lifecycle/' + s.orderId + '/cod-delivered', { deliveryReference: s.marker + '-LOCAL' })]);
        check('Concurrent committed collection replay succeeds', replayCollection.every(x => x.status === 200));
        s.settlementId = settlementId;
        await page.evaluate(s => sessionStorage.setItem('phase8-scenario', JSON.stringify(s)), s);
        const settlement = await api('post', '/payments/cod/manual-settlements', { settlementId, orderIds: [s.orderId] });
        check('Internal settlement posts EGP30 to Cashbox1010', settlement.status === 200 && settlement.data.accountingOutcome === 'Posted' && settlement.data.grossAmount === 30, { settlementId, journalId: settlement.data?.journalId, status: settlement.status });
        s.settlementJournalId = settlement.data.journalId;
        const replays = await Promise.all([api('post', '/payments/cod/manual-settlements', { settlementId, orderIds: [s.orderId] }), api('post', '/payments/cod/manual-settlements', { settlementId, orderIds: [s.orderId] })]);
        check('Concurrent settlement replay returns one journal', replays.every(x => x.status === 200 && x.data.journalId === s.settlementJournalId && x.data.wasIdempotent));
        const foreignOperation = await api('post', '/payments/cod/manual-settlements', { settlementId: '9577719a-d1d2-4cf9-be97-88ae1d32c467', orderIds: [s.orderId] });
        check('Another operation cannot settle allocated order again', foreignOperation.status === 409);
        check('Posted settled order completion succeeds', (await api('post', '/orders/' + s.orderId + '/fulfillment/complete', {})).status === 200);
        check('Completion replay is idempotent', (await api('post', '/orders/' + s.orderId + '/fulfillment/complete', {})).status === 200);
        check('Completed order cancellation is denied', (await api('post', '/admin/orders/' + s.orderId + '/cancel', { reason: s.marker + ' terminal negative' })).status === 409);
        const order = await api('get', '/admin/orders/' + s.orderId);
        const posted = await api('get', '/payments/cod/manual-settlements/' + settlementId);
        check('Fresh independent endpoints agree Completed/Settled/Posted30', order.data.status === 'Completed' && order.data.paymentStatus === 'Settled' && order.data.totals.total === 30 && posted.data.accountingOutcome === 'Posted');
        await p.goto('http://localhost:5173/admin/shipping/shipments/' + s.shipmentId, { waitUntil: 'domcontentloaded' });
        await p.getByText(s.marker + '-LOCAL', { exact: true }).first().waitFor();
        check('Actual Admin shipment UI displays retained local tracking', true);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally { await page.evaluate(s => sessionStorage.setItem('phase8-scenario', JSON.stringify(s)), s); }
    return { observedUtc: new Date().toISOString(), marker: s.marker, orderId: s.orderId, shipmentId: s.shipmentId, couponId: s.couponId, sessionId: s.sessionId, cartId: s.cartId, settlementId: s.settlementId, journalId: s.settlementJournalId, checks, failures };
}

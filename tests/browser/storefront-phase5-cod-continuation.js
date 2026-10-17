async page => {
    const orderId = 'a0755b91-1f50-442b-9c16-df9d32e11cfd', shipmentId = '4ebb25ac-0727-494c-9322-38942dfb9877';
    const settlementId = 'a640b716-b5ef-4eba-a7ce-9ff4b498cdb4';
    const checks = [], failures = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const api = (method, path, data) => page.evaluate(async ({ method, path, data }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message, error: e.response?.data?.error } }; }
    }, { method, path, data });
    let settlement;
    try {
        const eligible = await api('get', '/payments/cod/manual-settlements/eligible-orders');
        const selected = eligible.data.orders.find(o => o.orderId === orderId);
        check('Authoritative financial selector verifies Posted collection and exact EGP 32.35', selected?.eligible && selected.collectionStatus === 'Posted' && selected.amount === 32.35 && eligible.data.destinationAccountCode === '1010');
        const duplicateCollection = await Promise.all([api('post', '/payments/lifecycle/' + orderId + '/cod-delivered', { deliveryReference: 'P5QA20261017-817-LOCAL' }), api('post', '/payments/lifecycle/' + orderId + '/cod-delivered', { deliveryReference: 'P5QA20261017-817-LOCAL' })]);
        check('Committed collection replay is idempotent on competing calls', duplicateCollection.every(r => r.status === 200), { statuses: duplicateCollection.map(r => r.status) });
        settlement = await api('post', '/payments/cod/manual-settlements', { settlementId, orderIds: [orderId] });
        check('Synthetic internal settlement posts exact receiving Cashbox', settlement.status === 200 && settlement.data.accountingOutcome === 'Posted', { status: settlement.status, code: settlement.data?.code, result: settlement.data });
        const replay = await Promise.all([api('post', '/payments/cod/manual-settlements', { settlementId, orderIds: [orderId] }), api('post', '/payments/cod/manual-settlements', { settlementId, orderIds: [orderId] })]);
        check('Concurrent same-identity settlement replay retains one journal', replay.every(r => r.status === 200 && r.data.journalId === settlement.data.journalId && r.data.wasIdempotent));
        const readback = await api('get', '/payments/cod/manual-settlements/' + settlementId);
        check('Fresh settlement GET returns the same posted journal', readback.status === 200 && readback.data.journalId === settlement.data.journalId);
        const changed = await api('post', '/payments/cod/manual-settlements', { settlementId: '9a2c4d95-9d13-4537-b183-1c954c3df349', orderIds: [orderId] });
        check('New identity cannot settle allocated order again', changed.status === 409, { status: changed.status, code: changed.data.code });
        const complete = await api('post', '/orders/' + orderId + '/fulfillment/complete', {});
        check('Posted and settled order completion accepted', complete.status === 200, { status: complete.status, code: complete.data?.code });
        const order = await api('get', '/admin/orders/' + orderId);
        check('Fresh completed order retains separate Settled payment and original quote', order.data.status === 'Completed' && order.data.paymentStatus === 'Settled' && order.data.totals.shippingCost === 7.35, { status: order.data.status, paymentStatus: order.data.paymentStatus, total: order.data.totals.total });
        const cancel = await api('post', '/admin/orders/' + orderId + '/cancel', { reason: 'P5QA terminal negative test' });
        check('Completed posted order cannot be cancelled into another test state', cancel.status === 409);
        await page.goto('http://localhost:5173/admin/shipping/shipments/' + shipmentId);
        await page.getByText('P5QA20261017-817-LOCAL', { exact: true }).first().waitFor();
        check('Actual shipment details renders persisted synthetic tracking', true);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    return { orderId, shipmentId, settlementId, settlement: settlement?.data, checks, failures };
}

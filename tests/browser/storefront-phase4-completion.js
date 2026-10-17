async page => {
    const id = '258be5dc-92d9-4489-a1c4-0872d7f8b2b0', prefix = 'P4QA1792177832429';
    const checks = [], failures = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const api = (method, path, data) => page.evaluate(async ({ method, path, data }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message, error: e.response?.data?.error } }; }
    }, { method, path, data });
    const read = async path => { const r = await api('get', path); if (r.status !== 200) throw new Error('Read failed ' + r.status); return r.data; };
    try {
        await page.goto('http://localhost:5173/ecommerce/orders/' + id);
        await page.getByText('ORD-C9C5-2026-01055', { exact: true }).first().waitFor();
        let order = await read('/admin/orders/' + id);
        check('Fresh physical order is priced at EGP 40 despite conflicting legacy total echoes', order.totals.total === 40 && ['Pending', 'Confirmed', 'Processing'].includes(order.status), { status: order.status, paymentStatus: order.paymentStatus, total: order.totals.total });
        for (const [name, expected] of [['Confirm', 'Confirmed'], ['Pack', 'Packed']]) {
            if (order.fulfillmentStatus === expected) continue;
            await page.getByRole('button', { name: new RegExp(name, 'i') }).last().click();
            for (let i = 0; i < 20; i++) { order = await read('/admin/orders/' + id); if (order.fulfillmentStatus === expected) break; await page.waitForTimeout(300); }
            check('Actual Orders UI ' + name + ' transition persisted', order.fulfillmentStatus === expected, { fulfillmentStatus: order.fulfillmentStatus });
        }
        const dispatch = await api('post', '/orders/' + id + '/fulfillment/out-for-delivery', { shipmentId: order.shipment?.shipmentId, trackingNumber: prefix });
        check('Manual dispatch accepted with admitted shipment/tracking', dispatch.status === 200, { status: dispatch.status, message: dispatch.data?.message });
        const delivered = await api('post', '/orders/' + id + '/fulfillment/delivered', { deliveryReference: prefix, proofOfDeliveryUrl: 'https://example.test/p4qa/' + id, recipientName: 'Synthetic QA' });
        check('Synthetic Manual delivery accepted with explicit QA evidence', delivered.status === 200, { status: delivered.status, message: delivered.data?.message });
        const unpaid = await api('post', '/orders/' + id + '/fulfillment/complete', {});
        check('Delivered unpaid order cannot complete', unpaid.status === 409, { status: unpaid.status });
        const collect = await api('post', '/payments/lifecycle/' + id + '/cod-delivered', { deliveryReference: prefix });
        check('Internal test COD collection succeeds without a gateway transaction', collect.status === 200, { status: collect.status, code: collect.data?.code, message: collect.data?.message });
        for (let i = 0; i < 90; i++) { const a = await read('/orders/' + id + '/fulfillment/actions'); if (a.canComplete) break; await page.waitForTimeout(500); }
        check('Canonical financial authority now permits completion', (await read('/orders/' + id + '/fulfillment/actions')).canComplete);
        await page.reload();
        await page.getByRole('button', { name: /Complete/i }).last().click();
        for (let i = 0; i < 20; i++) { order = await read('/admin/orders/' + id); if (order.status === 'Completed') break; await page.waitForTimeout(300); }
        check('Actual Orders UI completion persists with separate Collected payment', order.status === 'Completed' && order.paymentStatus === 'Collected', { status: order.status, paymentStatus: order.paymentStatus, fulfillmentStatus: order.fulfillmentStatus });
        const duplicate = await api('post', '/orders/' + id + '/fulfillment/complete', {});
        check('Repeated completion is idempotent', duplicate.status === 200);
        const recollect = await api('post', '/payments/lifecycle/' + id + '/cod-delivered', { deliveryReference: prefix });
        check('Repeated internal collection is idempotent', recollect.status === 200);
        const cancelled = await api('post', '/admin/orders/' + id + '/cancel', { reason: prefix });
        check('Completed order rejects cancellation', cancelled.status === 409, { status: cancelled.status });
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    return { orderId: id, checks, failures };
}

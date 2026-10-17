async page => {
    const orderId = '45772d30-62df-4982-883e-852608e9f61f', paymentIntentId = '9ca01977-f535-4dd5-8860-6bc6c17831bd';
    const checks = [], failures = [], requests = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const api = (method, path, data) => page.evaluate(async ({ method, path, data }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data });
    const response = r => { if (r.url().includes('/api/')) requests.push({ path: r.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0], method: r.request().method(), status: r.status() }); };
    page.on('response', response);
    try {
        const order = await api('get', '/admin/orders/' + orderId);
        check('Exact committed order starts awaiting verification EGP 32.35', order.status === 200 && order.data.paymentStatus === 'AwaitingVerification' && order.data.totals.total === 32.35, { orderId, total: order.data.totals?.total });
        await page.goto('http://localhost:5173/ecommerce/manual-payments');
        const row = page.getByRole('row').filter({ has: page.getByText('ORD-C9C5-2026-01056', { exact: true }) });
        await row.waitFor();
        await row.getByRole('button', { name: /proof of payment/i }).click();
        await page.locator('img').last().waitFor();
        check('Actual protected stored QA proof image loads', await page.locator('img').last().evaluate(img => img.complete && img.naturalWidth > 0));
        await page.getByRole('button', { name: 'Close', exact: true }).last().click();
        await row.getByRole('button', { name: 'Reject', exact: true }).click();
        await page.locator('#manual-payment-rejection-reason').fill('P5QA20261017-817 Synthetic negative test: image explicitly NOT A RECEIPT; no money transferred.');
        await page.getByRole('button', { name: 'Reject Payment', exact: true }).click();
        await row.waitFor({ state: 'hidden' });
        const rejected = await api('get', '/admin/orders/' + orderId);
        check('Actual UI rejection persists separate Rejected payment', rejected.data.paymentStatus === 'Rejected', { status: rejected.data.status, paymentStatus: rejected.data.paymentStatus });
        const repeat = await api('post', '/admin/payments/manual-wallet/' + paymentIntentId + '/reject', { reason: 'P5QA duplicate rejection' });
        check('Repeated rejected review safely conflicts or replays', [200, 409].includes(repeat.status), { status: repeat.status, code: repeat.data?.code });
        const cancel = await api('post', '/admin/orders/' + orderId + '/cancel', { reason: 'P5QA20261017-817 rejected synthetic test cleanup' });
        check('Supported QA cancellation accepted', cancel.status === 200);
        let final;
        for (let i = 0; i < 100; i++) { final = await api('get', '/admin/orders/' + orderId); if (final.data.status === 'Cancelled') break; await page.waitForTimeout(500); }
        check('Fresh cancelled order readback remains unpaid', final.data.status === 'Cancelled' && !['Collected', 'Settled'].includes(final.data.paymentStatus), { status: final.data.status, paymentStatus: final.data.paymentStatus });
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally { page.off('response', response); }
    return { orderId, paymentIntentId, checks, failures, requests };
}

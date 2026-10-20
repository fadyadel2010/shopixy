async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const storefront = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    const target = await storefront.evaluate(() => JSON.parse(sessionStorage.getItem('mwvis-wallet-result')).body);
    const checks = [];
    const check = (name, passed) => { checks.push({name, passed: !!passed}); if (!passed) throw new Error(name); };
    if (target.orderId !== 'f17895bc-b5b0-43fb-87c9-cdb9da69494d') throw new Error('Unexpected QA target');
    const read = () => admin.evaluate(async target => {
        const api = (await import('/src/services/api.js')).default;
        return {details: (await api.get('/admin/orders/' + target.orderId)).data, list: (await api.get('/admin/orders', {params: {search: target.orderNumber, pageSize: 20}})).data};
    }, target);
    try {
        await admin.goto('http://localhost:5173/ecommerce/orders');
        await admin.getByPlaceholder('Order, customer, email, phone').fill(target.orderNumber);
        const before = await read();
        check('Pending wallet excluded from ordinary Orders', before.list.totalCount === 0);
        await admin.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/ordinary-orders-before-approval.png', fullPage: true});
        await admin.goto('http://localhost:5173/ecommerce/manual-payments');
        const row = admin.getByRole('row').filter({hasText: target.orderNumber});
        await row.waitFor();
        await admin.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/manual-payment-pending.png', fullPage: true});
        await row.getByRole('button', {name: /proof/i}).click();
        await admin.getByRole('button', {name: 'Close', exact: true}).waitFor();
        await admin.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/manual-payment-test-proof.png', fullPage: true});
        await admin.getByRole('button', {name: 'Close', exact: true}).click();
        await row.getByRole('button', {name: 'Approve', exact: true}).click();
        const response = admin.waitForResponse(r => r.url().includes('/' + target.paymentIntentId + '/confirm') && r.request().method() === 'POST');
        await admin.getByRole('button', {name: 'Confirm', exact: true}).click();
        const r = await response;
        const decision = {status: r.status(), body: await r.json()};
        check('Actual UI approval accepted', decision.status === 200);
        const after = await read();
        await admin.goto('http://localhost:5173/ecommerce/orders');
        await admin.getByPlaceholder('Order, customer, email, phone').fill(target.orderNumber);
        await admin.getByRole('row').filter({hasText: target.orderNumber}).waitFor();
        await admin.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/ordinary-orders-after-approval.png', fullPage: true});
        check('Approved wallet appears in ordinary Orders', after.list.totalCount === 1);
        return {checks, target, before, decision, after};
    } catch (error) { return {checks, failed: error.message, inspectPersistedStateBeforeRetry: true}; }
}

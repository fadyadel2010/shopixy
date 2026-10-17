async page => {
    const checks = [], failures = [], errors = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const p = await page.context().newPage();
    p.on('pageerror', e => errors.push(e.message.split('\n')[0]));
    try {
        await p.goto('http://ahramstore.localhost:5174/'); await p.locator('main').waitFor();
        for (const [id, status, payment] of [['45772d30-62df-4982-883e-852608e9f61f', 'Cancelled', 'Rejected'], ['a0755b91-1f50-442b-9c16-df9d32e11cfd', 'Completed', 'Settled']]) {
            const r = await p.evaluate(async id => {
                const { httpClient } = await import('/src/services/api/httpClient.ts');
                try { const r = await httpClient.get('/api/storefront/orders/' + id); return { status: r.status, data: { orderId: r.data.orderId || r.data.id, status: r.data.status, paymentStatus: r.data.paymentStatus, total: r.data.total, shippingCost: r.data.shippingCost } }; }
                catch (e) { return { status: e.response?.status || 0, code: e.response?.data?.code }; }
            }, id);
            check('Fresh customer-owned order read ' + id, r.status === 200 && r.data.status === status && r.data.paymentStatus === payment && r.data.total === 32.35, r);
        }
        const final = await p.evaluate(async () => {
            const { httpClient } = await import('/src/services/api/httpClient.ts');
            const cart = (await httpClient.get('/api/storefront/cart')).data;
            const methods = (await httpClient.get('/api/storefront/payment-methods')).data;
            const shipping = (await httpClient.get('/api/storefront/shipping-methods')).data;
            return { ownerType: cart.ownerType, itemCount: cart.items.length, paymentCodes: methods.paymentMethods.map(m => m.code), shippingIds: shipping.shippingMethods.map(m => m.id) };
        });
        check('Independent customer-owned cart is empty', final.ownerType.toLowerCase() === 'customer' && final.itemCount === 0, final);
        check('Public payment consumer excludes cleared wallet and retains COD', !final.paymentCodes.includes('MANUALWALLET') && final.paymentCodes.includes('COD'));
        check('Public shipping consumer excludes archived task fixture', !final.shippingIds.includes('3953e808-a29b-4003-ba7c-ab74827125fc'));
        check('No uncaught page errors during final customer readback', errors.length === 0);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally { await p.close(); }
    return { checks, failures, errors };
}

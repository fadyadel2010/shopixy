async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const storefront = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    await admin.goto('http://localhost:5173/ecommerce/orders');
    await admin.getByPlaceholder('Order, customer, email, phone').fill('ORD-C9C5-2026-01082');
    await admin.getByRole('row').filter({hasText: 'ORD-C9C5-2026-01082'}).waitFor();
    await admin.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/cod-ordinary-orders.png', fullPage: true});
    await admin.goto('http://localhost:5173/ecommerce/payment-methods');
    await admin.getByRole('row').filter({hasText: 'MANUALWALLET'}).getByRole('button', {name: 'Enable', exact: true}).waitFor();
    const config = await admin.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/payment-methods/MANUALWALLET')).data);
    await storefront.goto('http://ahramstore.localhost:5174/products');
    await storefront.getByRole('heading').first().waitFor();
    const publicResult = await storefront.evaluate(async () => {
        const r = await (await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/payment-methods');
        return {status: r.status, headers: r.headers, body: r.data};
    });
    return {config, publicResult, passed: !config.isEnabled && !config.isConfigurationValid && !config.isAvailableForCheckout && Object.keys(config.publicConfiguration).length === 0 && publicResult.body.paymentMethods.map(m => m.code).join(',') === 'COD'};
}

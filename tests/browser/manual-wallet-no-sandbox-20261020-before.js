async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const storefront = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    await admin.goto('http://localhost:5173/ecommerce/payment-methods');
    await admin.getByRole('row').filter({hasText: 'MANUALWALLET'}).waitFor();
    const methods = await admin.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/payment-methods')).data);
    const publicResult = await storefront.evaluate(async () => {
        const api = (await import('/src/services/api/httpClient.ts')).httpClient;
        const response = await api.get('/api/storefront/payment-methods');
        const config = await api.get('/api/storefront/store-config');
        return {status: response.status, headers: response.headers, body: response.data, tenant: {id: config.data.tenantId, name: config.data.storeName}};
    });
    await admin.getByRole('row').filter({hasText: 'MANUALWALLET'}).getByRole('button', {name:'Configure',exact:true}).click();
    await admin.screenshot({path: '.ai/evidence/manual-wallet-no-sandbox-20261020/admin-before.png', fullPage:true});
    const sandboxPresent = await admin.getByText('Sandbox Mode', {exact:true}).count();
    await admin.getByRole('button', {name:'Cancel',exact:true}).click();
    return {observedUtc:new Date().toISOString(), methods, publicResult, sandboxPresent};
}

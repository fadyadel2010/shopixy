async page => {
    const pages = page.context().pages();
    const admin = pages.find(p => p.url().startsWith('http://localhost:5173/'));
    const storefront = pages.find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    const methods = await admin.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/payment-methods')).data);
    const publicResult = await storefront.evaluate(async () => {
        const api = (await import('/src/services/api/httpClient.ts')).httpClient;
        const response = await api.get('/api/storefront/payment-methods');
        const config = await api.get('/api/storefront/store-config');
        return {status: response.status, headers: response.headers, body: response.data, tenant: {id: config.data.tenantId, name: config.data.storeName}};
    });
    return {observedUtc: new Date().toISOString(), adminUrl: admin.url(), storefrontUrl: storefront.url(), methods: methods.map(m => ({...m, publicConfiguration: {keys: Object.keys(m.publicConfiguration || {}), recipientPresent: !!m.publicConfiguration?.walletNumber}})), publicResult};
}

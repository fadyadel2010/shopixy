async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const methods = await admin.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/payment-methods')).data);
    const storefront = await page.evaluate(async () => {
        const api = (await import('/src/services/api/httpClient.ts')).httpClient;
        const read = async path => { try { const r = await api.get(path); return {status:r.status,body:r.data}; } catch (e) { return {status:e.response?.status,body:e.response?.data}; } };
        return {config:await read('/api/storefront/config'),cart:await read('/api/storefront/cart'),methods:await read('/api/storefront/payment-methods')};
    });
    return {observedUtc:new Date().toISOString(),adminUrl:admin.url(),storefrontUrl:page.url(),methods,storefront};
}

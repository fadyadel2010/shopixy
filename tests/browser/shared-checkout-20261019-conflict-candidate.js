async page => {
    // Negative request test, not response mocking: submit a stale coupon
    // candidate against real SQL reservation authority. Money is untouched.
    return await page.evaluate(async () => {
        const api = (await import('/src/services/api/httpClient.ts')).httpClient;
        sessionStorage.setItem('shqa-kind','CONFLICT-WALLET-COUPON');
        const id = api.interceptors.request.use(config => {
            if (config.method === 'post' && /\/api\/storefront\/checkout\/?$/.test((config.url || '').split('?')[0])) {
                api.interceptors.request.eject(id);
                const body = typeof config.data === 'string' ? JSON.parse(config.data) : config.data;
                config.data = {...body,couponCode:'SHQA20261019COD'};
            }
            return config;
        });
        return {negativeTest:'Next actual UI submission has stale already-reserved coupon candidate; real response and SQL authority, no mocked financial response',interceptorId:id};
    });
}

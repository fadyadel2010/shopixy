async page => {
    const checks = [], requests = [], errors = [];
    const listener = r => { if (r.url().includes('/api/')) requests.push({ path: r.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0], status: r.status(), method: r.request().method() }); };
    const error = e => errors.push(e.message);
    page.on('response', listener); page.on('pageerror', error);
    const api = path => page.evaluate(async path => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.get(path); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, code: e.response?.data?.code, message: e.response?.data?.message }; }
    }, path);
    try {
        for (const path of ['/ecommerce/payment-methods', '/ecommerce/manual-payments', '/admin/shipping?tab=methods', '/admin/shipping/shipments']) {
            await page.goto('http://localhost:5173' + path);
            await page.waitForTimeout(1600);
            checks.push({ route: path, headings: await page.locator('main h1,main h2').allTextContents(), text: (await page.locator('main').innerText()).slice(0, 700) });
        }
        for (const path of ['/admin/payment-methods', '/admin/payments/manual-wallet/pending', '/shippingmethods?activeOnly=false', '/admin/shipping/carrier-accounts', '/admin/shipping/supported-carriers']) {
            const r = await api(path);
            if (path.includes('manual-wallet')) r.data = r.data && { totalCount: r.data.totalCount, totalPages: r.data.totalPages };
            checks.push({ path, ...r });
        }
    } finally { page.off('response', listener); page.off('pageerror', error); }
    return { checks, requests, errors };
}

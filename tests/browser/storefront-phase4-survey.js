async page => {
    const checks = [], requests = [], errors = [];
    const paths = ['overview', 'orders', 'abandoned-checkouts', 'cart-recovery', 'blocked-phones', 'online-customers', 'whatsapp'];
    const listener = r => { if (r.url().includes('/api/')) requests.push({ path: r.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0], status: r.status(), method: r.request().method() }); };
    const error = e => errors.push(e.message);
    page.on('response', listener); page.on('pageerror', error);
    const api = (method, path, data) => page.evaluate(async ({ method, path, data }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: e.response?.data }; }
    }, { method, path, data });
    try {
        for (const path of paths) {
            await page.goto('http://localhost:5173/ecommerce/' + path);
            await page.locator('h1').first().waitFor();
            await page.waitForTimeout(1300);
            checks.push({ route: path, heading: await page.locator('h1').first().innerText(), visible: await page.locator('h1').first().isVisible() });
        }
        const config = await api('get', '/admin/store-config');
        checks.push({ name: 'tenant', passed: config.data?.tenantId === 'c9c5c1ae-949e-498e-bc63-07242daeb49d' });
        const invalid = await api('post', '/admin/phones/block', { phoneNumber: '123', reason: 'P4QA-invalid-validation' });
        checks.push({ name: 'invalid block rejected', passed: invalid.status === 400, status: invalid.status, persistedInvalid: invalid.data?.isBlocked === true });
        if (invalid.status === 200) {
            const restored = await api('post', '/admin/phones/unblock', { phoneNumber: '123' });
            checks.push({ name: 'invalid fixture unblocked', passed: restored.status === 200 && restored.data.isBlocked === false });
        }
        const stats = await api('get', '/admin/abandoned-checkouts/stats');
        checks.push({ name: 'checkout stats', status: stats.status, data: stats.data });
        const recovery = await api('get', '/admin/ecommerce/abandoned-cart-recovery/eligibility');
        checks.push({ name: 'recovery readiness', status: recovery.status, data: recovery.data });
    } finally { page.off('response', listener); page.off('pageerror', error); }
    return { checks, requests, errors };
}

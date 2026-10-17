// Read-only hosted Catalog display and product-template consumer; no cart/order actions.
async page => {
    const checks = [], failures = [], network = [], pageErrors = [], consoleErrors = [];
    const tab = await page.context().newPage();
    tab.on('pageerror', error => pageErrors.push(error.message));
    tab.on('console', message => { if (message.type() === 'error') consoleErrors.push({ category: message.text().startsWith('Failed to load resource') ? 'resource-failure' : 'application-error' }); });
    tab.on('response', response => { const path = response.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0]; if (path.startsWith('/api/storefront/')) network.push({ path, status: response.status() }); });
    const check = (name, passed, detail = {}) => { checks.push({ name, passed: Boolean(passed), ...detail }); if (!passed) throw new Error(name); };
    try {
        const response = await page.request.get('http://localhost:5174/api/storefront/products/discover?page=1&pageSize=12&lang=en', { headers: { Host: 'ahramstore.localhost:5174' } });
        const data = await response.json();
        check('Hosted Catalog discovery responds from the actual public API', response.status() === 200 && Array.isArray(data.items), { status: response.status(), items: data.items?.length, total: data.totalCount });
        await tab.goto('http://ahramstore.localhost:5174/products?lang=en');
        if (data.items.length) {
            const product = data.items[0];
            await tab.locator('main').getByText(product.name, { exact: true }).first().waitFor();
            check('Hosted Catalog renders actual discovered products, not only a loading surface', true);
            const sort = tab.getByRole('combobox').first();
            if (await sort.count()) check('Hosted Catalog exposes a usable sort control', await sort.isEnabled());
            const path = '/products/' + encodeURIComponent(product.slug || product.id);
            await tab.goto('http://ahramstore.localhost:5174' + path + '?lang=en');
            await tab.locator('main').getByRole('heading', { name: product.name, exact: true }).first().waitFor();
            check('Hosted Product Template consumer renders a real Product Details heading', true, { path });
            check('Product Template does not expose mandatory runtime sections as unknown', !/Unknown Section Type|Template unavailable/.test(await tab.locator('main').innerText()));
        } else {
            check('Hosted catalog is genuinely empty, not treated as a failed discovery', true);
        }
        check('Hosted Catalog/Product Details has no uncaught page errors', pageErrors.length === 0);
    } catch (error) { failures.push({ error: error.message.split('\n')[0], main: (await tab.locator('main').innerText().catch(() => '')).slice(0, 1200) }); }
    finally { await tab.close(); }
    return { evidence: 'hosted-catalog-read-only', checks, failures, network, pageErrors, consoleErrors, cartOrOrderMutations: 0 };
}

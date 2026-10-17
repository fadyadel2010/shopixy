// Hosted catalog reads and real client navigation; only traced read-only variant POST is allowed.
async page => {
    const context = await page.context().browser().newContext({ viewport: { width: 1280, height: 900 } });
    const tab = await context.newPage();
    const cases = [], blockedWrites = [], errors = [];
    tab.on('pageerror', error => errors.push(error.message));
    await tab.route('**/api/**', route => {
        const request = route.request();
        const path = request.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0];
        if (request.method() === 'POST' && /^\/api\/storefront\/products\/by-id\/[a-f0-9-]+\/variant-resolution$/.test(path)) return route.continue();
        if (path.startsWith('/api/') && !['GET', 'HEAD', 'OPTIONS'].includes(request.method())) {
            blockedWrites.push({ method: request.method(), path });
            return route.fulfill({ status: 409, contentType: 'application/json', body: '{"code":"QUALITY_READ_ONLY"}' });
        }
        return route.continue();
    });
    try {
        await tab.goto('http://ahramstore.localhost:5174/');
        await tab.locator('[data-product-card-theme]').first().waitFor();
        for (const theme of ['modern', 'minimal', 'fashion', 'pearl', 'phoenix', 'hype', 'alizon', 'sharp', 'cosmetics', 'seven']) {
            await tab.evaluate(async theme => {
                const source = await (await fetch('/src/storefront/runtime/layout/LayoutRuntime.tsx')).text();
                const url = source.match(/from "([^"]*\/src\/store\/index\.ts[^"]*)"/)[1];
                const { useStoreConfigStore } = await import(url);
                useStoreConfigStore.setState(s => ({ config: { ...s.config, storefrontTheme: theme } }));
            }, theme);
            await tab.locator(`[data-product-card-theme="${theme}"]`).first().waitFor();
            // Shared controls are siblings of the themed card surface to avoid nested links.
            const optionAction = tab.getByRole('link', { name: /^(Select options|اختر الخيارات)$/ }).first();
            await optionAction.waitFor();
            const href = await optionAction.getAttribute('href');
            const resolutionResponse = tab.waitForResponse(r => r.request().method() === 'POST' && r.url().includes('/variant-resolution'));
            await optionAction.click();
            await tab.waitForURL(url => url.pathname.startsWith('/products/') && url.pathname !== '/products/');
            await tab.locator('h1').waitFor();
            const resolution = await resolutionResponse;
            const resolved = resolution.ok() ? await resolution.json() : null;
            cases.push({ theme, selectOptionsNavigates: true, expectedDetailPath: href.split('?')[0] === tab.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0], variantResolutionStatus: resolution.status(), backendVariantResolved: !!resolved?.resolvedVariantId });
            await tab.goto('http://ahramstore.localhost:5174/');
            await tab.locator('[data-product-card-theme]').first().waitFor();
        }
        return { mode: 'hosted catalog GET + traced read-only variant POST + real Storefront navigation; theme override is browser memory only', cases, blockedWrites, errors };
    } catch (error) { return { cases, blockedWrites, errors, cards: await tab.locator('[data-product-card-theme]').allTextContents(), optionLinks: await tab.getByRole('link', { name: /Select|اختر/ }).allTextContents(), error: String(error) }; }
    finally { await context.close(); }
}

async page => {
    const tab = await page.context().newPage();
    const results = [], pageErrors = [];
    const check = (name, passed, evidence = {}) => { results.push({ name, passed, ...evidence }); if (!passed) throw new Error(name); };
    tab.on('pageerror', error => pageErrors.push(error.message));
    const id = '03144bba-1880-c1d6-3062-997a1006feca';
    const slug = 'cert-media-variable-20261007';
    const productUrl = 'http://ahramstore.localhost:5174/products/' + slug;
    let attemptedMutations = 0;
    await tab.route('**/api/storefront/**', route => {
        const request = route.request();
        const path = request.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0];
        if (!path.startsWith('/api/')) return route.continue();
        if (request.method() !== 'GET' && !path.endsWith('/variant-resolution') && !path.includes('/analytics') && !path.endsWith('/auth/refresh')) {
            attemptedMutations++;
            return route.abort();
        }
        return route.continue();
    });
    try {
        await tab.goto(productUrl);
        const hero = tab.locator('[data-section-type="product-hero"]');
        await hero.getByRole('heading', { name: 'CERT Media Variable 20261007', exact: true }).waitFor();
        const read = await tab.evaluate(async () => (await fetch('/api/storefront/products/by-id/03144bba-1880-c1d6-3062-997a1006feca')).json());
        check('hosted product name/category and price originate in backend DTO', (await hero.innerText()).includes(read.name) && (await hero.innerText()).includes(read.categoryName) && (await hero.innerText()).includes(read.pricing.currentPrice.toFixed(2)));
        for (const option of ['B', 'A']) {
            const pending = tab.waitForResponse(response => response.url().split('?')[0].endsWith(`/by-id/${id}/variant-resolution`) && response.request().method() === 'POST');
            await hero.getByRole('button', { name: option, exact: true }).click();
            const response = await pending;
            const data = await response.json();
            const sent = response.request().postDataJSON();
            check('option ' + option + ' submits candidates only and resolves at hosted backend', Object.keys(sent).length === 1 && sent.selectedOptions.Color === option && response.status() === 200 && data.productId === id && data.variant.id === data.resolvedVariantId,
                { http: response.status(), request: sent, backendVariantId: data.resolvedVariantId, cacheControl: response.headers()['cache-control'] });
            await hero.getByText(data.variant.availability.stockLabel, { exact: true }).first().waitFor();
            check('option ' + option + ' displays backend price/stock, not local matching truth', (await hero.innerText()).includes(data.variant.pricing.currentPrice.toFixed(2)));
        }
        const resolutionPattern = `**/api/storefront/products/by-id/${id}/variant-resolution*`;
        const foreign = async route => route.fulfill({ contentType: 'application/json', body: JSON.stringify({ productId: 'foreign', resolvedVariantId: read.variants[0].id, variant: { ...read.variants[0], pricing: { currentPrice: 0.01 } } }) });
        await tab.route(resolutionPattern, foreign);
        await hero.getByRole('button', { name: 'B', exact: true }).click();
        await hero.getByText('This combination is not available', { exact: true }).waitFor();
        check('controlled foreign variant response cannot authorize purchase or fabricated price', await hero.getByRole('button', { name: 'Out of Stock', exact: true }).isDisabled() && !(await hero.innerText()).includes('0.01'));
        await tab.unroute(resolutionPattern, foreign);
        await hero.getByRole('button', { name: 'A', exact: true }).click();
        await hero.getByRole('button', { name: 'Add to Cart', exact: true }).waitFor();
        check('real backend recovery restores valid purchase presentation', await hero.getByRole('button', { name: 'Add to Cart', exact: true }).isEnabled());

        const detailPattern = `**/api/storefront/products/${slug}*`;
        for (const [name, payload] of [
            ['missing pricing', { ...read, pricing: undefined }],
            ['mismatched route identity', { ...read, slug: 'foreign-slug', name: 'Forged name' }],
        ]) {
            const malformed = async route => route.fulfill({ contentType: 'application/json', body: JSON.stringify(payload) });
            await tab.route(detailPattern, malformed);
            await tab.reload();
            await tab.getByRole('heading', { name: 'Product not found', exact: true }).waitFor();
            check('controlled ' + name + ' fails closed without product/purchase UI', await tab.locator('#ai-product-context').count() === 0);
            await tab.unroute(detailPattern, malformed);
        }
        await tab.goto('http://ahramstore.localhost:5174/products/__phase4_missing_product__');
        await tab.getByRole('heading', { name: 'Product not found', exact: true }).waitFor();
        check('actual hosted missing product renders unavailable, not prior product', await tab.locator('#ai-product-context').count() === 0);
        check('no business mutation attempted and no page exceptions', attemptedMutations === 0 && pageErrors.length === 0);
        return { layer: 'hosted-product-and-controlled-rejection-UI', results, pageErrors, attemptedMutations };
    } catch (error) { return { results, pageErrors, attemptedMutations, error: error.message }; }
    finally { await tab.close(); }
}

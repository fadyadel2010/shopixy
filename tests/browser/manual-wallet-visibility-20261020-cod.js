async page => {
    const storefront = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    const checks = [];
    const check = (name, passed) => { checks.push({name, passed: !!passed}); if (!passed) throw new Error(name); };
    try {
        const cart = await storefront.evaluate(async () => (await (await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/cart')).data);
        check('Prior approved wallet checkout cleared only its own cart', cart.items.length === 0);
        await storefront.goto('http://ahramstore.localhost:5174/products/cert-media-simple-20261007');
        const add = storefront.waitForResponse(r => r.url().includes('/cart/items') && r.request().method() === 'POST');
        await storefront.getByRole('region', {name: 'Superstore buying confidence'}).getByRole('button', {name: 'Add to Cart', exact: true}).click();
        check('Real COD test item added', (await add).status() === 200);
        await storefront.goto('http://ahramstore.localhost:5174/cart');
        await storefront.getByRole('button', {name: 'Proceed to Checkout', exact: true}).click();
        await storefront.getByRole('textbox', {name: 'Full Name', exact: true}).fill('MWVIS20261020 COD TEST ONLY');
        await storefront.getByRole('textbox', {name: 'Phone Number', exact: true}).fill('1000000097');
        await storefront.getByRole('textbox', {name: 'Email Address', exact: true}).fill('wallet-visibility-qa@example.test');
        await storefront.getByRole('textbox', {name: 'City', exact: true}).fill('QA City');
        await storefront.getByRole('textbox', {name: 'Area / District', exact: true}).fill('QA Area');
        await storefront.getByRole('textbox', {name: 'Street Address', exact: true}).fill('TEST ONLY - NO DELIVERY');
        await storefront.getByRole('textbox', {name: 'Order Notes', exact: true}).fill('MWVIS20261020-COD TEST ONLY no payment no shipment');
        await storefront.getByRole('radio', {name: /^Standard Shipping/}).check();
        await storefront.getByRole('button', {name: 'Continue to Payment', exact: true}).click();
        const methods = await storefront.locator('input[name=paymentMethod]').evaluateAll(inputs => inputs.map(input => input.value));
        check('Only COD selectable with wallet restored disabled', methods.join(',') === 'COD');
        await storefront.getByText('Cash on Delivery', {exact: true}).click();
        await storefront.getByRole('button', {name: 'Review Order', exact: true}).click();
        await storefront.waitForFunction(() => [...document.querySelectorAll('button')].some(b => b.textContent.includes('Place Order') && !b.disabled));
        await storefront.evaluate(async () => {
            const loaded = performance.getEntriesByType('resource').find(e => e.name.includes('/src/services/api/httpClient.ts')).name;
            const {httpClient} = await import(loaded);
            httpClient.interceptors.response.use(r => {
                if (/\/api\/storefront\/checkout\/?$/.test((r.config.url || '').split('?')[0])) {
                    const body = {...r.data}; for (const key of Object.keys(body)) if (/secret|token/i.test(key)) delete body[key];
                    sessionStorage.setItem('mwvis-cod-result', JSON.stringify({status: r.status, body, request: JSON.parse(r.config.data || '{}')}));
                }
                return r;
            });
        });
        await storefront.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/cod-review-wallet-disabled.png', fullPage: true});
        const response = storefront.waitForResponse(r => /\/api\/storefront\/checkout\/?(?:\?|$)/.test(r.url()) && r.request().method() === 'POST', {timeout: 60000});
        await storefront.getByRole('button', {name: 'Place Order', exact: true}).click();
        const r = await response;
        await storefront.waitForURL(url => /\/checkout\/(pending|success)/.test(url.pathname), {timeout: 45000});
        const result = await storefront.evaluate(() => JSON.parse(sessionStorage.getItem('mwvis-cod-result') || 'null'));
        check('Independent COD checkout completed at authoritative EGP35', r.status() === 200 && result.body.totalAmount === 35 && result.body.paymentMethodCode === 'COD');
        await storefront.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/cod-completed-wallet-disabled.png', fullPage: true});
        return {checks, methods, result, text: await storefront.locator('main').innerText()};
    } catch (error) { return {checks, failed: error.message, inspectPersistedStateBeforeRetry: true}; }
}

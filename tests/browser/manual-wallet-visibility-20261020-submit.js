async page => {
    const storefront = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    const errors = [];
    const listener = error => errors.push(error.message);
    storefront.on('pageerror', listener);
    try {
        await storefront.getByText('P5QA20261017-817 TEST ONLY Wallet', {exact: true}).click();
        await storefront.getByRole('button', {name: 'VF Vodafone Cash', exact: true}).click();
        await storefront.getByPlaceholder('01xxxxxxxxx').fill('01000000097');
        await storefront.locator('input[type=file]').setInputFiles('.ai/evidence/manual-wallet-visibility-20261020/TEST-ONLY-NOT-A-REAL-RECEIPT.png');
        await storefront.getByRole('button', {name: 'Review Order', exact: true}).click();
        await storefront.waitForFunction(() => [...document.querySelectorAll('button')].some(b => b.textContent.includes('Place Order') && !b.disabled));
        const pricing = await storefront.evaluate(async () => {
            const loaded = performance.getEntriesByType('resource').find(e => e.name.includes('/src/store/checkoutStore.ts')).name;
            return (await import(loaded)).useCheckoutStore.getState().pricing;
        });
        if (pricing.total !== 35 || pricing.couponDiscount !== 0 || pricing.discount !== 0) throw new Error('Review totals must be confirmed before submission');
        await storefront.evaluate(async () => {
            const loaded = performance.getEntriesByType('resource').find(e => e.name.includes('/src/services/api/httpClient.ts')).name;
            const {httpClient} = await import(loaded);
            const capture = r => {
                if (/\/api\/storefront\/checkout\/?$/.test((r.config.url || '').split('?')[0])) {
                    const body = {...r.data};
                    for (const key of Object.keys(body)) if (/token|secret/i.test(key)) delete body[key];
                    sessionStorage.setItem('mwvis-wallet-result', JSON.stringify({status: r.status, body, request: JSON.parse(r.config.data || '{}')}));
                }
                return r;
            };
            httpClient.interceptors.response.use(capture, e => { if (e.response) capture(e.response); return Promise.reject(e); });
        });
        await storefront.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/wallet-final-review.png', fullPage: true});
        const response = storefront.waitForResponse(r => /\/api\/storefront\/checkout\/?(?:\?|$)/.test(r.url()) && r.request().method() === 'POST', {timeout: 60000});
        await storefront.getByRole('button', {name: 'Place Order', exact: true}).click();
        const r = await response;
        if (r.status() === 200) await storefront.waitForURL(url => /\/checkout\/(pending|success)/.test(url.pathname), {timeout: 45000});
        const result = await storefront.evaluate(() => JSON.parse(sessionStorage.getItem('mwvis-wallet-result') || 'null'));
        await storefront.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/wallet-pending-result.png', fullPage: true});
        return {pricing, result, status: r.status(), errors, text: await storefront.locator('main').innerText()};
    } catch (error) { return {failed: error.message, errors, inspectPersistedStateBeforeRetry: true, url: storefront.url()}; }
    finally { storefront.off('pageerror', listener); }
}

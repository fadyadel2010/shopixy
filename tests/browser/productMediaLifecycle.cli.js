// Run in the registered headed opencode-ui-test session after creating the
// disposable fixtures documented in the certification report. No payments.
async page => {
    const base = 'http://ahramstore.localhost:5174';
    const slugs = ['cert-media-simple-20261007', 'cert-media-variable-20261007'];
    const products = await page.evaluate(async slugs => Promise.all(slugs.map(async slug => {
        const response = await fetch(`/api/storefront/products/${slug}`);
        if (!response.ok) throw new Error(`Product read ${response.status}`);
        return response.json();
    })), slugs);
    const evidence = [];
    const failures = [];
    const mediaResponse = response => {
        if (response.url().includes('/api/upload/product-media/') && response.status() !== 200)
            failures.push({ url: response.url(), status: response.status() });
    };
    page.on('response', mediaResponse);
    try {
        for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
            await page.setViewportSize(viewport);
            for (const language of ['en', 'ar']) {
                await page.goto(`${base}/`);
                await page.getByRole('link', { name: 'AhramStore', exact: true }).first().waitFor();
                if (await page.locator('html').getAttribute('lang') !== language) {
                    if (viewport.width < 1024) await page.getByRole('button', {name: /^(Menu|القائمة)$/}).click();
                    await page.getByRole('button', { name: language === 'ar' ? 'Switch to Arabic' : 'Switch to English', exact: true }).click();
                    if (viewport.width < 1024) await page.getByRole('button', {name: /^(Menu|القائمة)$/}).click();
                }
                for (const route of ['/', '/products', `/products/${slugs[0]}`, `/products/${slugs[1]}`, '/cart', '/checkout']) {
                    const cartResponse = ['/cart', '/checkout'].includes(route)
                        ? page.waitForResponse(r => r.url().split('?')[0].endsWith('/api/storefront/cart') && r.request().method() === 'GET')
                        : null;
                    await page.goto(`${base}${route}`);
                    await page.locator('main').waitFor();
                    let expected = [];
                    if (cartResponse) {
                        const response = await cartResponse;
                        const cart = await response.json();
                        expected = cart.items.filter(line => products.some(p => p.id === line.productId)).map(line => {
                            const product = products.find(p => p.id === line.productId);
                            const variant = product.variants.find(v => v.id === line.variantId);
                            const images = product.media.filter(m => m.mediaType === 'image').sort((a,b) => a.position-b.position);
                            const media = product.hasVariants ? images.find(m => m.id === variant?.mediaId) || images[0] : images[0];
                            if (line.thumbnailImage !== media?.url) throw new Error(`Cart media mismatch ${line.variantId}`);
                            return media.url;
                        });
                        if (expected.length !== 4) throw new Error(`Expected four disposable cart lines, got ${expected.length}`);
                        if (route === '/checkout' && viewport.width < 1024)
                            await page.locator('button[aria-controls=order-summary-content]').click();
                    } else if (route.startsWith('/products/')) {
                        const product = products.find(p => route.endsWith(p.slug));
                        const images = product.media.filter(m => m.mediaType === 'image').sort((a,b) => a.position-b.position);
                        expected = [product.hasVariants ? product.variants.find(v => v.isDefault).imageUrl : images[0].url];
                    } else {
                        expected = products.map(product => product.media.filter(m => m.mediaType === 'image').sort((a,b) => a.position-b.position)[0].url);
                    }
                    for (const path of expected) {
                        const image = route.startsWith('/products/') ? page.locator('main img').first() : page.locator(`main img[src="${path}"]`).first();
                        await image.scrollIntoViewIfNeeded();
                        await page.waitForFunction(({path,detail}) => {
                            const images=detail ? [document.querySelector('main img')] : [...document.querySelectorAll('main img')];
                            return images.some(i => i?.getAttribute('src') === path && i.complete && i.naturalWidth === 240 && i.getBoundingClientRect().width > 0);
                        }, {path,detail:route.startsWith('/products/')});
                    }
                    const actual = await page.locator('main img').evaluateAll(imgs => imgs.filter(i=>i.currentSrc.includes('/api/upload/product-media/')).map(i=>({src:i.currentSrc,width:i.naturalWidth,height:i.naturalHeight,visible:i.getBoundingClientRect().width>0})));
                    if (await page.locator('html').getAttribute('dir') !== (language === 'ar' ? 'rtl' : 'ltr')) throw new Error('Direction mismatch');
                    evidence.push({ route, language, viewport, expected, actual });
                    if (route === '/checkout') await page.locator('#order-summary-content').screenshot({path:`.artifacts/media-cert-${language}-${viewport.width}-checkout.png`});
                }
            }
        }
        if (failures.length) throw new Error(JSON.stringify(failures));
        return { checkpoints: evidence.length, failures, evidence };
    } catch (error) {
        await page.screenshot({ path: '.artifacts/media-cert-sweep-failure.png', fullPage: true });
        throw error;
    } finally {
        page.off('response', mediaResponse);
    }
}

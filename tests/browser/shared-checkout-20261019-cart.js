async page => {
    const kind = await page.evaluate(() => sessionStorage.getItem('shqa-kind') || 'BASELINE-COD-NONE');
    const coupon = await page.evaluate(() => sessionStorage.getItem('shqa-coupon'));
    const cart = await page.evaluate(async () => (await (await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/cart')).data);
    if (cart.items.length) throw Error('Cart is not empty: inspect rather than overwrite');
    const product = await page.evaluate(() => sessionStorage.getItem('shqa-product') || 'iphone-17-pro-max');
    if (!['iphone-17-pro-max','cert-media-simple-20261007'].includes(product)) throw Error('Unexpected QA product');
    await page.goto('http://ahramstore.localhost:5174/products/'+product);
    const added = page.waitForResponse(r => r.url().includes('/cart/items') && r.request().method() === 'POST');
    await page.getByRole('region', {name:'Superstore buying confidence'}).getByRole('button', {name:'Add to Cart',exact:true}).click();
    const add = await added;
    if (add.status() !== 200) throw Error('Add failed: ' + add.status());
    await page.goto('http://ahramstore.localhost:5174/cart');
    await page.getByRole('button', {name:'Proceed to Checkout',exact:true}).waitFor();
    let applied;
    if (coupon) {
        await page.getByRole('textbox', {name:'Enter coupon code',exact:true}).fill(coupon);
        const response = page.waitForResponse(r => r.url().includes('apply-coupon') && r.request().method() === 'POST');
        await page.getByRole('button', {name:'Apply',exact:true}).click();
        const r = await response;
        applied = {status:r.status(),body:await r.json()};
        if (r.status() !== 200) return {kind,applied};
    }
    const text = await page.locator('main').innerText();
    await page.screenshot({path:'.ai/evidence/shared-checkout-20261019/'+kind.toLowerCase()+'-cart.png',fullPage:true});
    await page.getByRole('button', {name:'Proceed to Checkout',exact:true}).click();
    return {kind,applied,cartText:text};
}

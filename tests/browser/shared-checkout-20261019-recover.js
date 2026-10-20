async page => {
    const prior = await page.evaluate(() => JSON.parse(sessionStorage.getItem('SHQA20261019-FINAL-COD-NONE-http')));
    if (prior?.status !== 500 || prior.request?.cartId !== 'e632128f-ecf9-406c-84e7-865d4990429f') throw Error('Unexpected interrupted request; inspect instead');
    const outcome = await page.evaluate(async request => {
        try {
            const r = await (await import('/src/services/api/httpClient.ts')).httpClient.post('/api/storefront/checkout',request);
            return {status:r.status,body:r.data};
        } catch (e) { return {status:e.response?.status,body:e.response?.data}; }
    },prior.request);
    if (outcome.status !== 200 || outcome.body.orderId !== '297dd7e8-be38-454f-8c80-b6872417d93b') throw Error('Recovery failed: inspect SQL before retry: '+JSON.stringify(outcome));
    await page.goto('http://ahramstore.localhost:5174/checkout/pending?orderId='+outcome.body.orderId+'&orderNumber='+outcome.body.orderNumber);
    await page.screenshot({path:'.ai/evidence/shared-checkout-20261019/interrupted-cod-recovered.png',fullPage:true});
    await page.goto('http://ahramstore.localhost:5174/cart');
    const cart = await page.evaluate(async () => (await (await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/cart')).data);
    if (cart.items.length) {
        if (cart.cartId !== prior.request.cartId) throw Error('Unrelated cart appeared; do not clear');
        await page.getByRole('button',{name:'Clear Cart',exact:true}).click();
    }
    return {outcome,oldCart:cart,text:await page.locator('main').innerText()};
}

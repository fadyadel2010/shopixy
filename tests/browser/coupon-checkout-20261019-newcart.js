async page => {
    const cart=await page.evaluate(async()=>(await (await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/cart')).data);
    if(cart.items.length) throw Error('Cart not empty; inspect instead of overwriting');
    const kind=await page.evaluate(()=>sessionStorage.getItem('ccqa-kind')||'WALLET-BEFORE');
    const code=await page.evaluate(()=>sessionStorage.getItem('ccqa-coupon')||'CCQA20261019WALLET');
    await page.goto('http://ahramstore.localhost:5174/products/iphone-17-pro-max');
    const added=page.waitForResponse(r=>r.url().includes('/cart/items')&&r.request().method()==='POST');
    await page.getByRole('region',{name:'Superstore buying confidence'}).getByRole('button',{name:'Add to Cart',exact:true}).click();
    await added;
    await page.goto('http://ahramstore.localhost:5174/cart');
    await page.getByRole('button',{name:'Proceed to Checkout',exact:true}).waitFor();
    if(!kind.includes('NOCOUPON')) {
        const coupon=page.getByRole('textbox',{name:'Enter coupon code',exact:true});
        if(await coupon.isVisible()) {
            await coupon.fill(code);
            const response=page.waitForResponse(r=>r.url().includes('apply-coupon')&&r.request().method()==='POST');
            await page.getByRole('button',{name:'Apply',exact:true}).click();
            const r=await response;
            if(r.status()!==200) return {kind,status:r.status(),body:await r.json()};
        }
    }
    const text=await page.locator('main').innerText();
    await page.getByRole('button',{name:'Proceed to Checkout',exact:true}).click();
    return {kind,cartText:text};
}

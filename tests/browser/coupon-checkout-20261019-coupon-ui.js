async page => {
    const checks=[],network=[];
    const check=(name,passed,data)=>{checks.push({name,passed:!!passed,data});if(!passed)throw Error(name);};
    const cart=()=>page.evaluate(async()=>(await (await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/cart')).data);
    const apply=async code=>{
        await page.getByRole('textbox',{name:'Enter coupon code',exact:true}).fill(code);
        const response=page.waitForResponse(r=>r.url().includes('apply-coupon')&&r.request().method()==='POST');
        await page.getByRole('button',{name:'Apply',exact:true}).click();
        const r=await response;const body=await r.json();network.push({operation:'apply',code,status:r.status(),body});return {status:r.status(),body};
    };
    const remove=async()=>{
        const response=page.waitForResponse(r=>r.url().includes('remove-coupon'));
        await page.getByRole('button',{name:'Remove',exact:true}).click();
        const r=await response;network.push({operation:'remove',status:r.status(),body:await r.json()});
        await page.getByRole('textbox',{name:'Enter coupon code',exact:true}).waitFor();
    };
    try {
        await page.goto('http://ahramstore.localhost:5174/cart');
        await page.getByRole('button',{name:'Remove',exact:true}).waitFor();
        check('Accepted coupon on 550 gross is 27.50',(await cart()).discountTotal===27.5);
        await page.reload();await page.getByRole('button',{name:'Remove',exact:true}).waitFor();
        check('Refresh retains coupon once',(await cart()).discountTotal===27.5);
        await remove();check('Removal restores authoritative 550 merchandise total',(await cart()).grandTotal===550);
        const invalid=await apply('CCQA20261019INVALID');check('Invalid coupon rejected',invalid.status===400);
        const missing=await apply('1441');check('Missing reported coupon rejected, not fabricated',missing.status===400);
        check('Invalid codes do not discount basket',(await cart()).discountTotal===0);
        const reserved=await apply('CCQA20261019FIVE');
        check('Pending-order reservation rejected with actionable domain message',reserved.status===400&&JSON.stringify(reserved.body).includes('COUPON_RESERVATION_EXISTS'));
        check('Valid independent coupon accepted',(await apply('CCQA20261019WALLET')).status===200);
        await remove();await apply('CCQA20261019WALLET');
        check('Remove/reapply does not stack',(await cart()).discountTotal===27.5);
        const changed=page.waitForResponse(r=>r.url().includes('/cart/items')&&r.request().method()==='PUT');
        await page.getByRole('button',{name:'Increase quantity',exact:true}).click();
        await changed;
        const two=await cart();check('Quantity two reprices 1100 less 55',two.subtotal===1100&&two.discountTotal===55&&two.grandTotal===1045,two);
        const back=page.waitForResponse(r=>r.url().includes('/cart/items')&&r.request().method()==='PUT');
        await page.getByRole('button',{name:'Decrease quantity',exact:true}).click();await back;
        check('Quantity one restores exact non-stacked discount',(await cart()).discountTotal===27.5);
        await page.screenshot({path:'.ai/evidence/coupon-checkout-20261019/postfix-coupon-cart.png',fullPage:true});
        await page.getByRole('button',{name:'Proceed to Checkout',exact:true}).click();
        return {checks,network};
    } catch(e) { return {checks,network,failure:e.message,text:await page.locator('main').innerText()}; }
}

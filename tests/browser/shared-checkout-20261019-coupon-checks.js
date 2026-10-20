async page => {
    const checks = [], network = [];
    const check = (name,passed) => { checks.push({name,passed:!!passed}); if (!passed) throw Error(name); };
    const cart = () => page.evaluate(async () => (await (await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/cart')).data);
    const apply = async code => {
        await page.getByRole('textbox',{name:'Enter coupon code',exact:true}).fill(code);
        const response = page.waitForResponse(r => r.url().includes('apply-coupon') && r.request().method()==='POST');
        await page.getByRole('button',{name:'Apply',exact:true}).click();
        const r=await response; const body=await r.json(); network.push({operation:'apply',code,status:r.status(),body}); return {status:r.status(),body};
    };
    const remove = async () => {
        const response=page.waitForResponse(r=>r.url().includes('remove-coupon'));
        await page.getByRole('button',{name:'Remove',exact:true}).click();
        const r=await response; network.push({operation:'remove',status:r.status(),body:await r.json()});
        await page.getByRole('textbox',{name:'Enter coupon code',exact:true}).waitFor();
    };
    try {
        await page.goto('http://ahramstore.localhost:5174/cart');
        await page.getByRole('button',{name:'Remove',exact:true}).waitFor();
        check('Five percent applied once',(await cart()).discountTotal===27.5);
        await page.reload(); await page.getByRole('button',{name:'Remove',exact:true}).waitFor();
        check('Refresh retains one discount',(await cart()).discountTotal===27.5);
        await remove(); check('Removal restores gross',(await cart()).grandTotal===550);
        check('Invalid code controlled 400',(await apply('SHQA20261019INVALID')).status===400);
        check('Unavailable original 1441 controlled 400',(await apply('1441')).status===400);
        const conflict=await apply('SHQA20261019COD');
        check('Active reservation controlled actionable rejection',conflict.status===400 && JSON.stringify(conflict.body).includes('COUPON_RESERVATION_EXISTS'));
        await page.screenshot({path:'.ai/evidence/shared-checkout-20261019/coupon-reservation-conflict.png',fullPage:true});
        check('Independent wallet coupon valid',(await apply('SHQA20261019WALLET')).status===200);
        await remove(); await apply('SHQA20261019WALLET');
        check('Remove/reapply does not stack',(await cart()).discountTotal===27.5);
        const changed=page.waitForResponse(r=>r.url().includes('/cart/items')&&r.request().method()==='PUT');
        await page.getByRole('button',{name:'Increase quantity',exact:true}).click(); const increased=await changed;
        network.push({operation:'increase',status:increased.status(),body:await increased.json()});
        const two=await cart(); check('Quantity two recalculates 1100 less 55',two.subtotal===1100 && two.discountTotal===55 && two.grandTotal===1045);
        const back=page.waitForResponse(r=>r.url().includes('/cart/items')&&r.request().method()==='PUT');
        await page.getByRole('button',{name:'Decrease quantity',exact:true}).click(); await back;
        check('Quantity one restores exact discount',(await cart()).discountTotal===27.5);
        await page.screenshot({path:'.ai/evidence/shared-checkout-20261019/coupon-revalidated.png',fullPage:true});
        await page.getByRole('button',{name:'Proceed to Checkout',exact:true}).click();
        return {checks,network};
    } catch(e) { return {checks,network,failure:e.message,text:await page.locator('main').innerText()}; }
}

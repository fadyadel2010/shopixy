async page => {
    const a=page.context().pages()[0];
    const target=await page.evaluate(()=>JSON.parse(sessionStorage.getItem('ccqa-review')||'null'));
    if(!target?.orderNumber?.includes('2026-')&&!target?.orderNumber?.startsWith('CCQA-')) throw Error('Missing exact QA review target');
    const read=()=>a.evaluate(async id=>{const api=(await import('/src/services/api.js')).default;return {details:(await api.get('/admin/orders/'+id)).data,list:(await api.get('/admin/orders',{params:{search:id,pageSize:20}})).data};},target.orderId);
    await a.goto('http://localhost:5173/ecommerce/orders');
    await a.getByRole('heading',{name:'Orders',exact:true}).waitFor();
    const before=await read();
    await a.screenshot({path:'.ai/evidence/coupon-checkout-20261019/'+target.label+'-ordinary-before.png',fullPage:true});
    await a.goto('http://localhost:5173/ecommerce/manual-payments');
    const row=a.getByRole('row').filter({hasText:target.orderNumber});
    await row.waitFor();
    await a.screenshot({path:'.ai/evidence/coupon-checkout-20261019/'+target.label+'-pending.png',fullPage:true});
    await row.getByRole('button',{name:/proof/i}).click();
    await a.getByRole('button',{name:'Close',exact:true}).waitFor();
    await a.screenshot({path:'.ai/evidence/coupon-checkout-20261019/'+target.label+'-proof.png',fullPage:true});
    await a.getByRole('button',{name:'Close',exact:true}).click();
    await row.getByRole('button',{name:'Approve',exact:true}).click();
    const response=a.waitForResponse(r=>r.url().includes('/'+target.paymentIntentId+'/confirm')&&r.request().method()==='POST');
    let concurrentDecision;
    if(target.concurrent) {
        const second=await a.context().newPage();
        await second.goto('http://localhost:5173/ecommerce/manual-payments');
        await second.getByRole('row').filter({hasText:target.orderNumber}).getByRole('button',{name:'Approve',exact:true}).click();
        const other=second.waitForResponse(r=>r.url().includes('/'+target.paymentIntentId+'/confirm')&&r.request().method()==='POST');
        await Promise.all([a.getByRole('button',{name:'Confirm',exact:true}).click(),second.getByRole('button',{name:'Confirm',exact:true}).click()]);
        const r2=await other;concurrentDecision={status:r2.status(),body:await r2.json()};
        await second.screenshot({path:'.ai/evidence/coupon-checkout-20261019/'+target.label+'-concurrent-decision.png',fullPage:true});
        await second.close();
    } else await a.getByRole('button',{name:'Confirm',exact:true}).click();
    const r=await response;
    const decision={status:r.status(),body:await r.json()};
    await a.screenshot({path:'.ai/evidence/coupon-checkout-20261019/'+target.label+'-decision.png',fullPage:true});
    const after=await read();
    return {target,before,decision,concurrentDecision,after,text:await a.locator('main').innerText()};
}

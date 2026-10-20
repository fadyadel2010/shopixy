async page=>{
    const a=page.context().pages()[0];
    await a.goto('http://localhost:5173/ecommerce/payment-methods');
    const before=await a.evaluate(async()=>{
        const api=(await import('/src/services/api.js')).default;
        return {pending:(await api.get('/admin/payments/manual-wallet/pending')).data,method:(await api.get('/admin/payment-methods/MANUALWALLET')).data};
    });
    if(before.pending.items.some(p=>p.orderNumber.startsWith('CCQA-')||['ORD-C9C5-2026-01065','ORD-C9C5-2026-01066','ORD-C9C5-2026-01067','ORD-C9C5-2026-01068','ORD-C9C5-2026-01069','ORD-C9C5-2026-01070'].includes(p.orderNumber))) throw Error('Task review still pending; inspect instead of cleanup');
    const row=a.getByRole('row').filter({hasText:'MANUALWALLET'});
    await row.getByRole('button',{name:'Configure',exact:true}).click();
    const input=a.locator('#payment-config-MANUALWALLET-walletNumber');
    if(await input.inputValue()!=='01000000000')throw Error('Independent config change; do not overwrite');
    const form=a.locator('form').filter({has:input});
    await form.getByRole('checkbox').nth(1).check();
    await form.getByRole('checkbox').nth(2).check();
    const response=a.waitForResponse(r=>r.url().includes('/MANUALWALLET/configure')&&r.request().method()==='PUT');
    await form.getByRole('button',{name:/Reset Configuration/i}).click();
    const r=await response;await input.waitFor({state:'hidden'});
    await a.reload();await row.getByRole('button',{name:'Enable',exact:true}).waitFor();
    await a.screenshot({path:'.ai/evidence/coupon-checkout-20261019/final-wallet-disabled.png',fullPage:true});
    const after=await a.evaluate(async()=>{
        const api=(await import('/src/services/api.js')).default;
        const method=(await api.get('/admin/payment-methods/MANUALWALLET')).data;
        const coupons=[];
        for(const id of ['da4bcdb5-af72-4b2e-bd2a-8e0a89ee119a','a42fb624-167c-4196-9e29-66ab26614493','28df2da4-6fab-418b-ab9a-cf26592e2456']){
            const c=(await api.get('/coupons/'+id)).data;
            if(!c.code.startsWith('CCQA20261019'))throw Error('Not task coupon');
            if(c.isActive)await api.patch('/coupons/'+id+'/toggle',{}, {headers:{'If-Match':'"'+c.rowVersion+'"'}});
            coupons.push((await api.get('/coupons/'+id)).data);
        }
        return {method,coupons};
    });
    const publicMethods=await page.evaluate(async()=>(await (await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/payment-methods')).data);
    return {before,resetStatus:r.status(),after,publicMethods,syntheticRecipientRemoved:true};
}

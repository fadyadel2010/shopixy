async page => {
    const admin=page.context().pages().find(p=>p.url().startsWith('http://localhost:5173/'));
    await admin.goto('http://localhost:5173/ecommerce/payment-methods');
    const before=await admin.evaluate(async()=>{
        const api=(await import('/src/services/api.js')).default;
        return {pending:(await api.get('/admin/payments/manual-wallet/pending')).data,method:(await api.get('/admin/payment-methods/MANUALWALLET')).data};
    });
    const taskOrders=['ORD-C9C5-2026-01073','ORD-C9C5-2026-01076','ORD-C9C5-2026-01078','ORD-C9C5-2026-01079','SHQA-20261019-UNDERPAY-2d59dc7c'];
    if (before.pending.items.some(p=>taskOrders.includes(p.orderNumber))) throw Error('Task reviews remain pending: inspect before restoration');
    const row=admin.getByRole('row').filter({hasText:'MANUALWALLET'});
    await row.getByRole('button',{name:'Configure',exact:true}).click();
    const input=admin.locator('#payment-config-MANUALWALLET-walletNumber');
    if (await input.inputValue()!=='01000000000') throw Error('Independent recipient change: do not overwrite');
    const form=admin.locator('form').filter({has:input});
    await form.getByRole('checkbox').nth(1).check();
    await form.getByRole('checkbox').nth(2).check();
    const response=admin.waitForResponse(r=>r.url().includes('/MANUALWALLET/configure')&&r.request().method()==='PUT');
    await form.getByRole('button',{name:/Reset Configuration/i}).click();
    const r=await response; await input.waitFor({state:'hidden'});
    await admin.reload(); await row.getByRole('button',{name:'Enable',exact:true}).waitFor();
    await admin.screenshot({path:'.ai/evidence/shared-checkout-20261019/final-wallet-disabled.png',fullPage:true});
    const identities=await page.evaluate(()=>JSON.parse(sessionStorage.getItem('shqa-coupons')));
    const after=await admin.evaluate(async ids=>{
        const api=(await import('/src/services/api.js')).default;
        const coupons=[];
        for (const entry of ids) {
            const c=(await api.get('/coupons/'+entry.body.id)).data;
            if (!c.code.startsWith('SHQA20261019')) throw Error('Not a task coupon');
            if (c.isActive) await api.patch('/coupons/'+c.id+'/toggle',{}, {headers:{'If-Match':'"'+c.rowVersion+'"'}});
            coupons.push((await api.get('/coupons/'+c.id)).data);
        }
        return {method:(await api.get('/admin/payment-methods/MANUALWALLET')).data,cod:(await api.get('/admin/payment-methods/COD')).data,coupons};
    },identities);
    const publicMethods=await page.evaluate(async()=>(await (await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/payment-methods')).data);
    if (after.method.isEnabled || Object.keys(after.method.publicConfiguration).length) throw Error('Synthetic configuration not removed');
    return {before,resetStatus:r.status(),after,publicMethods};
}

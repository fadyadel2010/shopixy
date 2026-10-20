async page=>{
    const a=page.context().pages()[0];
    const prior=await a.evaluate(async()=>{
        const api=(await import('/src/services/api.js')).default;
        return {pending:(await api.get('/admin/payments/manual-wallet/pending')).data,methods:(await api.get('/admin/payment-methods')).data};
    });
    if(prior.pending.items.some(p=>p.orderNumber.startsWith('MWQA-20261019')||['ORD-C9C5-2026-01059','ORD-C9C5-2026-01060','ORD-C9C5-2026-01062'].includes(p.orderNumber)))throw Error('QA review decision still pending');
    await a.goto('http://localhost:5173/ecommerce/payment-methods');
    const row=a.getByRole('row').filter({hasText:'MANUALWALLET'});
    await row.getByRole('button',{name:'Configure',exact:true}).click();
    const input=a.locator('#payment-config-MANUALWALLET-walletNumber');
    if(await input.inputValue()!=='01000000000')throw Error('Configuration changed independently; do not overwrite');
    const form=a.locator('form').filter({has:input});
    await form.getByRole('checkbox').nth(1).check();
    await form.getByRole('checkbox').nth(2).check();
    const response=a.waitForResponse(r=>r.url().includes('/MANUALWALLET/configure')&&r.request().method()==='PUT');
    await form.getByRole('button',{name:/Reset Configuration/i}).click();
    const r=await response;
    await input.waitFor({state:'hidden'});
    await a.reload();await row.getByRole('button',{name:'Enable',exact:true}).waitFor();
    await a.screenshot({path:'.ai/evidence/manual-wallet-20261019/final-wallet-disabled.png',fullPage:true});
    const final=await a.evaluate(async()=>{
        const api=(await import('/src/services/api.js')).default;
        const methods=(await api.get('/admin/payment-methods')).data;
        let attempt;try{const r=await api.post('/admin/payment-methods/MANUALWALLET/enable',{});attempt={status:r.status};}catch(e){attempt={status:e.response?.status,data:e.response?.data};}
        return {methods,enableMissingRecipient:attempt,whatsapp:(await api.get('/admin/whatsapp/accounts')).data.map(x=>({id:x.id,enabled:x.enabled}))};
    });
    const anonymous=await page.context().browser().newContext();
    const res=await anonymous.request.get('http://localhost:5174/api/storefront/payment-methods',{headers:{Host:'ahramstore.localhost:5174'}});
    const publicMethods={status:res.status(),data:await res.json()};await anonymous.close();
    return {resetStatus:r.status(),originalVerifiedRecipient:false,final,publicMethods,syntheticRecipientRemoved:true};
}

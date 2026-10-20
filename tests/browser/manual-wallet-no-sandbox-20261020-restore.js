async page => {
    const admin=page.context().pages().find(p=>p.url().startsWith('http://localhost:5173/'));
    const storefront=page.context().pages().find(p=>p.url().startsWith('http://ahramstore.localhost:5174/'));
    await admin.goto('http://localhost:5173/ecommerce/payment-methods');
    const row=admin.getByRole('row').filter({hasText:'MANUALWALLET'});
    await row.getByRole('button',{name:'Configure',exact:true}).click();
    const input=admin.locator('#payment-config-MANUALWALLET-walletNumber');
    if(await input.inputValue()!=='01000000000')throw new Error('Unexpected independent recipient; do not overwrite');
    const form=admin.locator('form').filter({has:input});
    await form.getByRole('checkbox').nth(0).check();
    await form.getByRole('checkbox').nth(1).check();
    const response=admin.waitForResponse(r=>r.url().includes('/MANUALWALLET/configure')&&r.request().method()==='PUT');
    await form.getByRole('button',{name:/Reset Configuration/i}).click();
    const r=await response;
    await input.waitFor({state:'hidden'});
    await admin.reload();
    await row.getByRole('button',{name:'Enable',exact:true}).waitFor();
    const readback=await admin.evaluate(async()=>{
        const api=(await import('/src/services/api.js')).default;let enable;
        try{const r=await api.post('/admin/payment-methods/MANUALWALLET/enable',{});enable={status:r.status};}
        catch(e){enable={status:e.response?.status,body:e.response?.data};}
        return{wallet:(await api.get('/admin/payment-methods/MANUALWALLET')).data,methods:(await api.get('/admin/payment-methods')).data,enable};
    });
    const publicResult=await storefront.evaluate(async()=>{const r=await(await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/payment-methods');return{status:r.status,headers:r.headers,body:r.data};});
    await admin.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/final-disabled-empty.png',fullPage:true});
    return{resetStatus:r.status(),...readback,publicResult,passed:r.status()===200&&!readback.wallet.isEnabled&&!readback.wallet.isConfigurationValid&&Object.keys(readback.wallet.publicConfiguration).length===0&&readback.enable.status===400};
}

async page => {
    const admin = page.context().pages()[0];
    const call = (method,path,data) => admin.evaluate(async ({method,path,data}) => {
        try { const r = await (await import('/src/services/api.js')).default.request({method,url:path,data}); return {status:r.status,data:r.data}; }
        catch(e) { return {status:e.response?.status||0,data:e.response?.data}; }
    },{method,path,data});
    const reset = await call('put','/admin/payment-methods/MANUALWALLET/configure',{reset:true,confirmReset:true});
    if(reset.status!==200 || reset.data.isEnabled || reset.data.isConfigurationValid) throw Error('Safe reset failed');
    const pendingRequest = page.waitForRequest(r=>r.url().endsWith('/api/storefront/checkout')&&r.method()==='POST');
    const pendingResponse = page.waitForResponse(r=>r.url().endsWith('/api/storefront/checkout')&&r.request().method()==='POST');
    await page.getByRole('button',{name:'Place Order',exact:true}).click();
    const req = await pendingRequest, res=await pendingResponse;
    const body=req.postDataJSON();
    await page.evaluate(body=>sessionStorage.setItem('mwqa-checkout-body',JSON.stringify(body)),body);
    const data=await res.json();
    await page.screenshot({path:'.ai/evidence/manual-wallet-20261019/stale-checkout-rejected.png',fullPage:true});
    const restored=await call('put','/admin/payment-methods/MANUALWALLET/configure',{configuration:{walletNumber:'01000000000'}});
    const enabled=await call('post','/admin/payment-methods/MANUALWALLET/enable',{});
    return {reset:{status:reset.status,enabled:reset.data.isEnabled,valid:reset.data.isConfigurationValid},checkout:{status:res.status(),data},body,restored:restored.status,enabled:enabled.status};
}

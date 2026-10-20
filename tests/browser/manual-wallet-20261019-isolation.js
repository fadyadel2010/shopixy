async page=>{
    const a=page.context().pages()[0];
    const admin=await a.evaluate(async()=>{
        const api=(await import('/src/services/api.js')).default;
        const original=(await api.get('/admin/payment-methods')).data;
        const foreign='11111111-1111-4111-8111-111111111111';
        const probes=[];
        for(const [method,url,data] of [
            ['get','/admin/payment-methods/MANUALWALLET'],
            ['put','/admin/payment-methods/MANUALWALLET/configure',{configuration:{walletNumber:'01000000088'}}],
            ['get','/admin/orders/82af88bc-bfce-478d-b323-7d37706b6e66'],
            ['post','/admin/payments/manual-wallet/4c614307-2ba1-4e8e-b0f4-e38342f1807e/confirm',{}],
            ['post','/admin/payments/manual-wallet/4c614307-2ba1-4e8e-b0f4-e38342f1807e/reject',{reason:'MWQA foreign tenant request must fail'}],
            ['get','/admin/payments/manual-wallet/proof-image/4c614307-2ba1-4e8e-b0f4-e38342f1807e']
        ]) {
            try{const r=await api.request({method,url,data,headers:{'X-Tenant-Id':foreign}});probes.push({method,url,status:r.status});}
            catch(e){probes.push({method,url,status:e.response?.status||0,code:e.response?.data?.code,message:e.response?.data?.message});}
        }
        return {probes,configurationUnchanged:JSON.stringify(original)===JSON.stringify((await api.get('/admin/payment-methods')).data),whatsapp:(await api.get('/admin/whatsapp/accounts')).data.map(x=>({id:x.id,enabled:x.enabled}))};
    });
    const publicProbes=await page.evaluate(async()=>{
        const {getRequestHeaders}=await import('/src/bootstrap/index.ts');
        const results=[];
        for(const path of ['/payment-methods','/payments/manual-wallet/config']){
            const r=await fetch('/api/storefront'+path,{headers:{...getRequestHeaders(),'X-Tenant-Id':'11111111-1111-4111-8111-111111111111'},credentials:'include'});
            results.push({path,status:r.status});
        }
        return results;
    });
    return {admin,publicProbes,scope:'Host/claimed tenant mismatch negatives; separately configured service-context tests cover another actual tenant authority'};
}

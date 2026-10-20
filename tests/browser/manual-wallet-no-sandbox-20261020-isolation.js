async page => {
    const admin=page.context().pages().find(p=>p.url().startsWith('http://localhost:5173/'));
    const storefront=page.context().pages().find(p=>p.url().startsWith('http://ahramstore.localhost:5174/'));
    const target=await storefront.evaluate(()=>JSON.parse(sessionStorage.getItem('mwnosb-wallet-result')).body);
    const result=await admin.evaluate(async target=>{
        const api=(await import('/src/services/api.js')).default;
        const original=(await api.get('/admin/payment-methods/MANUALWALLET')).data;
        const request=async(method,url,data,headers={})=>{
            try{const r=await api.request({method,url,data,headers});return{status:r.status,body:r.data};}
            catch(e){return{status:e.response?.status,body:e.response?.data};}
        };
        const foreign=[];
        for(const [method,url,data] of [
            ['get','/admin/payment-methods/MANUALWALLET'],
            ['put','/admin/payment-methods/MANUALWALLET/configure',{configuration:{walletNumber:'01000000088'}}],
            ['get','/admin/orders/'+target.orderId],
            ['post','/admin/payments/manual-wallet/'+target.paymentIntentId+'/confirm',{}],
            ['get','/admin/payments/manual-wallet/proof-image/'+target.paymentIntentId]
        ])foreign.push({method,url,...await request(method,url,data,{'X-Tenant-Id':'11111111-1111-4111-8111-111111111111'})});
        const invalid=[];
        for(const value of['','not-a-wallet'])invalid.push(await request('put','/admin/payment-methods/MANUALWALLET/configure',{configuration:{walletNumber:value}}));
        const duplicate=await Promise.all([1,2].map(()=>request('post','/admin/payments/manual-wallet/'+target.paymentIntentId+'/confirm',{})));
        const staleRejection=await request('post','/admin/payments/manual-wallet/'+target.paymentIntentId+'/reject',{reason:'MWNOSB20261020 stale decision must fail'});
        const unchanged=JSON.stringify(original)===JSON.stringify((await api.get('/admin/payment-methods/MANUALWALLET')).data);
        const proof=await api.get('/admin/payments/manual-wallet/proof-image/'+target.paymentIntentId,{responseType:'blob'});
        const dataUrl=await new Promise(resolve=>{const reader=new FileReader();reader.onload=()=>resolve(reader.result);reader.readAsDataURL(proof.data);});
        const tab=window.open('','_blank');const img=tab.document.createElement('img');img.src=dataUrl;img.alt='Actual retained TEST ONLY proof';tab.document.body.append(img);
        return{foreign,invalid,duplicate,staleRejection,unchanged,proof:{status:proof.status,type:proof.data.type,size:proof.data.size}};
    },target);
    const proofPage=page.context().pages().find(p=>p.url()==='about:blank');
    await proofPage.waitForFunction(()=>document.querySelector('img')?.complete&&document.querySelector('img').naturalWidth>0);
    await proofPage.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/actual-retained-proof.png',fullPage:true});
    await proofPage.close();
    const publicProbes=await storefront.evaluate(async target=>{
        const loaded=performance.getEntriesByType('resource').find(e=>e.name.includes('/src/bootstrap/index.ts'))?.name||'/src/bootstrap/index.ts';
        const{getRequestHeaders}=await import(loaded);const probes=[];
        for(const path of['/payment-methods','/payments/manual-wallet/config','/payments/'+target.paymentIntentId]){
            const r=await fetch('/api/storefront'+path,{headers:{...getRequestHeaders(),'X-Tenant-Id':'11111111-1111-4111-8111-111111111111'},credentials:'include'});probes.push({path,status:r.status});
        }return probes;
    },target);
    return{...result,publicProbes,passed:result.unchanged&&result.foreign.every(r=>r.status===403)&&result.invalid.every(r=>r.status===400)&&result.duplicate.every(r=>r.status>=400)&&result.staleRejection.status>=400&&publicProbes.every(r=>r.status===403)};
}

async page=>{
    const a=page.context().pages()[0];
    await a.goto('http://localhost:5173/ecommerce/manual-payments');
    await a.getByRole('row').filter({hasText:'ORD-C9C5-2026-01062'}).waitFor();
    const result=await a.evaluate(async()=>{
        const api=(await import('/src/services/api.js')).default;
        const pending=(await api.get('/admin/payments/manual-wallet/pending')).data.items.find(p=>p.orderId==='b58e0c99-08e4-401c-9c1a-4ad19cc5b0e8');
        if(!pending)throw Error('Exact QA pending payment missing');
        const decide=async(kind)=>{try{const r=await api.post('/admin/payments/manual-wallet/'+pending.paymentIntentId+'/'+kind,{reason:'MWQA stale rejection'}, {headers:{'Idempotency-Key':'MWQA20261019-race-'+kind+'-'+crypto.randomUUID()}});return {status:r.status,data:r.data};}catch(e){return {status:e.response?.status||0,data:e.response?.data};}};
        const approvals=await Promise.all([decide('confirm'),decide('confirm')]);
        const staleRejection=await decide('reject');
        return {paymentIntentId:pending.paymentIntentId,approvals,staleRejection,details:(await api.get('/admin/orders/'+pending.orderId)).data};
    });
    // Use the deliberately stale real screen after the concurrent server decisions.
    await a.getByRole('row').filter({hasText:'ORD-C9C5-2026-01062'}).getByRole('button',{name:'Approve',exact:true}).click();
    const response=a.waitForResponse(r=>r.url().includes('/'+result.paymentIntentId+'/confirm'));
    await a.getByRole('button',{name:'Confirm',exact:true}).click();
    const r=await response;
    result.staleUiApproval={status:r.status(),data:await r.json()};
    await a.screenshot({path:'.ai/evidence/manual-wallet-20261019/stale-decision-refused.png',fullPage:true});
    return result;
}

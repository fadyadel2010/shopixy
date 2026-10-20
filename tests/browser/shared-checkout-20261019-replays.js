async page => {
    const kind = await page.evaluate(() => sessionStorage.getItem('shqa-kind'));
    const replay = await page.evaluate(async kind => {
        const api = (await import('/src/services/api/httpClient.ts')).httpClient;
        const prior = JSON.parse(sessionStorage.getItem('SHQA20261019-'+kind+'-http'));
        const send = async () => {
            try { const r = await api.post('/api/storefront/checkout',prior.request); return {status:r.status,orderId:r.data.orderId,orderNumber:r.data.orderNumber,total:r.data.totalAmount,state:r.data.status}; }
            catch (e) { return {status:e.response?.status,body:e.response?.data}; }
        };
        return {originalOrderId:prior.body.orderId,responses:await Promise.all([send(),send()])};
    },kind);
    if (!replay.responses.every(r => r.status===200 && r.orderId===replay.originalOrderId)) throw Error('Replay did not recover exactly the same order');
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const target = await page.evaluate(() => JSON.parse(sessionStorage.getItem('shqa-review') || 'null'));
    const decisions = await admin.evaluate(async t => {
        const api = (await import('/src/services/api.js')).default;
        const request = async (method,url,data,headers) => {
            try { const r=await api.request({method,url,data,headers}); return {status:r.status,body:r.data}; }
            catch (e) { return {status:e.response?.status,body:e.response?.data}; }
        };
        const foreign = {'X-Tenant-Id':'11111111-1111-4111-8111-111111111111'};
        return {staleApprove:await request('post','/admin/payments/manual-wallet/'+t.paymentIntentId+'/confirm',{}),
            staleReject:await request('post','/admin/payments/manual-wallet/'+t.paymentIntentId+'/reject',{reason:'SHQA stale decision must fail'}),
            foreignRead:await request('get','/admin/orders/'+t.orderId,null,foreign),
            foreignDecision:await request('post','/admin/payments/manual-wallet/'+t.paymentIntentId+'/confirm',{},foreign),
            foreignProof:await request('get','/admin/payments/manual-wallet/proof-image/'+t.paymentIntentId,null,foreign)};
    },target);
    return {replay,decisions};
}

async page=>{
    const replay=await page.evaluate(async()=>{
        const {httpClient}=await import('/src/services/api/httpClient.ts');
        const prior=JSON.parse(sessionStorage.getItem('CCQA20261019-WALLET-CERTIFIED-http'));
        const send=async()=>{try{const r=await httpClient.post('/api/storefront/checkout',prior.request,{headers:{'Cache-Control':'no-store',Pragma:'no-cache'}});return {status:r.status,orderId:r.data.orderId,orderNumber:r.data.orderNumber,total:r.data.totalAmount,state:r.data.status};}catch(e){return {status:e.response?.status,body:e.response?.data};}};
        return {originalOrderId:prior.body.orderId,responses:await Promise.all([send(),send()])};
    });
    const a=page.context().pages()[0];
    const decisions=await a.evaluate(async()=>{
        const api=(await import('/src/services/api.js')).default;
        const id='61ea6884-5342-4c2a-8dbe-a5aede2a8030';
        const results=[];
        for(const kind of ['confirm','reject']){try{const r=await api.post('/admin/payments/manual-wallet/'+id+'/'+kind,{reason:'CCQA20261019 stale decision must fail'});results.push({kind,status:r.status});}catch(e){results.push({kind,status:e.response?.status,body:e.response?.data});}}
        const foreign='11111111-1111-4111-8111-111111111111';
        const probes=[];
        for(const [method,url,data] of [['get','/admin/orders/a5a0b6b7-7926-4c6d-827a-ad362665b613'],['post','/admin/payments/manual-wallet/'+id+'/confirm',{}],['get','/admin/payments/manual-wallet/proof-image/'+id]]){
            try{const r=await api.request({method,url,data,headers:{'X-Tenant-Id':foreign}});probes.push({method,url,status:r.status});}catch(e){probes.push({method,url,status:e.response?.status,code:e.response?.data?.code});}
        }
        return {stale:results,tenantMismatch:probes};
    });
    await a.goto('http://localhost:5173/ecommerce/orders');
    await a.getByRole('heading',{name:'Orders',exact:true}).waitFor();
    await a.getByPlaceholder('Order, customer, email, phone').fill('ORD-C9C5-2026-01070');
    await a.getByRole('row').filter({hasText:'ORD-C9C5-2026-01070'}).waitFor();
    await a.screenshot({path:'.ai/evidence/coupon-checkout-20261019/certified-coupon-ordinary-after.png',fullPage:true});
    await a.reload();await a.getByRole('heading',{name:'Orders',exact:true}).waitFor();
    return {replay,decisions,ordersText:await a.locator('main').innerText()};
}

async page => {
    const admin=page.context().pages().find(p=>p.url().startsWith('http://localhost:5173/'));
    const t=await page.evaluate(()=>JSON.parse(sessionStorage.getItem('shqa-review')||'null'));
    if (!t?.label?.startsWith('shqa-') || !t.orderId || !t.paymentIntentId) throw Error('Missing exact QA target');
    const read=()=>admin.evaluate(async t=>{
        const api=(await import('/src/services/api.js')).default;
        return {details:(await api.get('/admin/orders/'+t.orderId)).data,ordinary:(await api.get('/admin/orders',{params:{search:t.orderNumber}})).data};
    },t);
    await admin.goto('http://localhost:5173/ecommerce/manual-payments');
    await admin.getByRole('button',{name:'Refresh',exact:true}).waitFor();
    const before=await read();
    if (['Rejected','Expired'].includes(before.details.paymentStatus)) return {target:t,before,alreadyTerminal:true};
    const row=admin.getByRole('row').filter({hasText:t.orderNumber});
    await row.getByRole('button',{name:'Reject',exact:true}).click();
    await admin.getByRole('textbox',{name:/Rejection Reason/}).fill('SHQA20261019 TEST ONLY QA cleanup; no transfer or delivery occurred');
    const response=admin.waitForResponse(r=>r.url().includes('/'+t.paymentIntentId+'/reject')&&r.request().method()==='POST');
    await admin.getByRole('button',{name:'Reject Payment',exact:true}).click();
    const r=await response;
    const decision={status:r.status(),body:await r.json()};
    await row.waitFor({state:'hidden'});
    await admin.screenshot({path:'.ai/evidence/shared-checkout-20261019/'+t.label+'-rejected.png',fullPage:true});
    return {target:t,before,decision,after:await read()};
}

async page => {
    const admin=page.context().pages().find(p=>p.url().startsWith('http://localhost:5173/'));
    const storefront=page.context().pages().find(p=>p.url().startsWith('http://ahramstore.localhost:5174/'));
    const target=await storefront.evaluate(()=>JSON.parse(sessionStorage.getItem('mwnosb-wallet-result')).body);
    if(!target.orderId||!target.paymentIntentId)throw new Error('Missing task checkout identity');
    const read=()=>admin.evaluate(async target=>{
        const api=(await import('/src/services/api.js')).default;
        return{details:(await api.get('/admin/orders/'+target.orderId)).data,list:(await api.get('/admin/orders',{params:{search:target.orderNumber,pageSize:20}})).data};
    },target);
    const before=await read();
    if(!before.details.customerNotes?.startsWith('MWNOSB20261020'))throw new Error('Not task-owned QA order');
    if(before.list.totalCount!==0)throw new Error('Pending wallet present in ordinary Orders');
    await admin.goto('http://localhost:5173/ecommerce/orders');
    await admin.getByPlaceholder('Order, customer, email, phone').fill(target.orderNumber);
    await admin.getByText('No orders matched the current tenant filters.',{exact:true}).waitFor();
    await admin.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/orders-before-approval.png',fullPage:true});
    await admin.goto('http://localhost:5173/ecommerce/manual-payments');
    const row=admin.getByRole('row').filter({hasText:target.orderNumber});
    await row.waitFor();
    await admin.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/manual-payment-pending.png',fullPage:true});
    await row.getByRole('button',{name:/proof/i}).click();
    await admin.getByRole('button',{name:'Close',exact:true}).waitFor();
    await admin.locator('img').filter({visible:true}).last().waitFor();
    await admin.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/manual-payment-proof.png',fullPage:true});
    await admin.getByRole('button',{name:'Close',exact:true}).click();
    await row.getByRole('button',{name:'Approve',exact:true}).click();
    const response=admin.waitForResponse(r=>r.url().includes('/'+target.paymentIntentId+'/confirm')&&r.request().method()==='POST');
    await admin.getByRole('button',{name:'Confirm',exact:true}).click();
    const r=await response;
    const decision={status:r.status(),body:await r.json()};
    if(decision.status!==200)return{failed:'Approval failed; inspect before retry',target,before,decision};
    const after=await read();
    await admin.goto('http://localhost:5173/ecommerce/orders');
    await admin.getByPlaceholder('Order, customer, email, phone').fill(target.orderNumber);
    await admin.getByRole('row').filter({hasText:target.orderNumber}).waitFor();
    await admin.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/orders-after-approval.png',fullPage:true});
    return{target,before,decision,after,passed:after.list.totalCount===1};
}

async page => {
    const admin=page.context().pages().find(p=>p.url().startsWith('http://localhost:5173/'));
    const storefront=page.context().pages().find(p=>p.url().startsWith('http://ahramstore.localhost:5174/'));
    const target=await storefront.evaluate(()=>JSON.parse(sessionStorage.getItem('mwnosb-wallet-result')).body);
    const before=await admin.evaluate(async target=>(await(await import('/src/services/api.js')).default.get('/admin/orders/'+target.orderId)).data,target);
    if(!before.customerNotes?.startsWith('MWNOSB20261020')||before.paymentStatus!=='AwaitingVerification')throw new Error('Not a fresh task-owned QA review');
    await admin.goto('http://localhost:5173/ecommerce/manual-payments');
    const row=admin.getByRole('row').filter({hasText:target.orderNumber});
    await row.waitFor();
    await row.getByRole('button',{name:'Reject',exact:true}).click();
    return {target,before,text:await admin.locator('main').innerText(),decisionNotSubmitted:true};
}

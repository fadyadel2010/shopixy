async page => {
    const admin=page.context().pages().find(p=>p.url().startsWith('http://localhost:5173/'));
    const storefront=page.context().pages().find(p=>p.url().startsWith('http://ahramstore.localhost:5174/'));
    const target=await storefront.evaluate(()=>JSON.parse(sessionStorage.getItem('mwnosb-wallet-result')).body);
    await admin.locator('#manual-payment-rejection-reason').fill('MWNOSB20261020 TEST ONLY synthetic receipt rejected; no transfer occurred.');
    await admin.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/rejection-confirmation.png',fullPage:true});
    const response=admin.waitForResponse(r=>r.url().includes('/'+target.paymentIntentId+'/reject')&&r.request().method()==='POST');
    await admin.getByRole('button',{name:'Reject Payment',exact:true}).click();
    const r=await response;
    const decision={status:r.status(),body:await r.json()};
    const after=await admin.evaluate(async target=>(await(await import('/src/services/api.js')).default.get('/admin/orders/'+target.orderId)).data,target);
    return{target,decision,after,passed:r.status()===200&&after.paymentStatus==='Rejected'};
}

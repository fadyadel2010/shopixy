async page=>{
    const a=page.context().pages()[0];
    await a.goto('http://localhost:5173/ecommerce/manual-payments');
    const row=a.getByRole('row').filter({hasText:'ORD-C9C5-2026-01060'});
    await row.waitFor();
    const before=await a.evaluate(async()=>{const api=(await import('/src/services/api.js')).default;return {pending:(await api.get('/admin/payments/manual-wallet/pending')).data,ordinary:(await api.get('/admin/orders?search=ORD-C9C5-2026-01060')).data};});
    await row.getByRole('button',{name:'Reject',exact:true}).click();
    await a.getByRole('textbox',{name:/Rejection Reason/}).fill('MWQA20261019 TEST ONLY rejected synthetic proof; no transfer occurred');
    const response=a.waitForResponse(r=>r.url().includes('/manual-wallet/')&&r.url().includes('/reject')&&r.request().method()==='POST');
    await a.getByRole('button',{name:'Reject Payment',exact:true}).click();
    const r=await response;
    const data=await r.json();
    await row.waitFor({state:'hidden'});
    await a.screenshot({path:'.ai/evidence/manual-wallet-20261019/rejected-review-list.png',fullPage:true});
    await a.goto('http://localhost:5173/ecommerce/orders/2c909e2d-8da6-40c1-b9c5-1e1a6c06f849');
    await a.getByText('ORD-C9C5-2026-01060',{exact:true}).first().waitFor();
    await a.screenshot({path:'.ai/evidence/manual-wallet-20261019/rejected-audit-details.png',fullPage:true});
    const after=await a.evaluate(async()=>{const api=(await import('/src/services/api.js')).default;return {ordinary:(await api.get('/admin/orders?search=ORD-C9C5-2026-01060')).data,details:(await api.get('/admin/orders/2c909e2d-8da6-40c1-b9c5-1e1a6c06f849')).data};});
    return {before,decision:{status:r.status(),data},after};
}

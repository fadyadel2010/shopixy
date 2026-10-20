async page=>{
    const a=page.context().pages()[0];
    await a.goto('http://localhost:5173/ecommerce/manual-payments');
    await a.getByRole('button',{name:'Refresh',exact:true}).click();
    const row=a.getByRole('row').filter({hasText:'MWQA-20261019-UNDERPAY-82af88bc'});
    await row.waitFor();
    const before=await row.innerText();
    await row.getByRole('button',{name:'Approve',exact:true}).click();
    const response=a.waitForResponse(r=>r.url().includes('/4c614307-2ba1-4e8e-b0f4-e38342f1807e/confirm'));
    await a.getByRole('button',{name:'Confirm',exact:true}).click();
    const r=await response; const data=await r.json();
    await a.getByText(/Submitted amount 25.00 EGP does not cover/).first().waitFor();
    await a.screenshot({path:'.ai/evidence/manual-wallet-20261019/underpayment-rejected.png',fullPage:true});
    const persisted=await a.evaluate(async()=>{const api=(await import('/src/services/api.js')).default;return {ordinary:(await api.get('/admin/orders?search=MWQA-20261019-UNDERPAY-82af88bc')).data,details:(await api.get('/admin/orders/82af88bc-bfce-478d-b323-7d37706b6e66')).data};});
    return {fixture:'Production-admitted explicit synthetic underpayment; no Storefront amount field exists',before,status:r.status(),data,persisted};
}

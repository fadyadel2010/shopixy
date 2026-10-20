async page => {
    const admin=page.context().pages().find(p=>p.url().startsWith('http://localhost:5173/'));
    const storefront=page.context().pages().find(p=>p.url().startsWith('http://ahramstore.localhost:5174/'));
    const cod=await storefront.evaluate(()=>JSON.parse(sessionStorage.getItem('mwnosb-cod-result')).body);
    await admin.goto('http://localhost:5173/ecommerce/orders');
    await admin.getByPlaceholder('Order, customer, email, phone').fill(cod.orderNumber);
    await admin.getByRole('row').filter({hasText:cod.orderNumber}).waitFor();
    await admin.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/cod-ordinary-orders.png',fullPage:true});
    const state=await admin.evaluate(async()=>{
        const api=(await import('/src/services/api.js')).default;
        const session=(await api.get('/auth/session')).data;
        return {wallet:(await api.get('/admin/payment-methods/MANUALWALLET')).data,identity:{email:session.user?.email,tenantId:session.user?.tenantId,role:session.user?.role}};
    });
    await storefront.goto('http://ahramstore.localhost:5174/');
    await storefront.getByRole('link',{name:'Shop',exact:true}).waitFor();
    const publicResult=await storefront.evaluate(async()=>{
        const api=(await import('/src/services/api/httpClient.ts')).httpClient;
        const r=await api.get('/api/storefront/payment-methods');const customer=(await api.get('/api/storefront/auth/me')).data;
        return{body:r.data,headers:r.headers,customer:{email:customer.email,id:customer.id}};
    });
    await admin.goto('http://localhost:5173/ecommerce/payment-methods');
    await admin.getByRole('row').filter({hasText:'MANUALWALLET'}).getByRole('button',{name:'Enable',exact:true}).waitFor();
    await admin.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/closeout-admin.png',fullPage:true});
    return{state,publicResult,cod,passed:!state.wallet.isEnabled&&Object.keys(state.wallet.publicConfiguration).length===0&&publicResult.body.paymentMethods.map(m=>m.code).join(',')==='COD'};
}

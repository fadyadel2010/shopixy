async page => {
    const admin=page.context().pages().find(p=>p.url().startsWith('http://localhost:5173/'));
    await admin.goto('http://localhost:5173/ecommerce/orders');
    await admin.getByPlaceholder('Order, customer, email, phone').fill('ORD-C9C5-2026-01080');
    await admin.getByRole('row').filter({hasText:'ORD-C9C5-2026-01080'}).waitFor();
    await admin.screenshot({path:'.ai/evidence/shared-checkout-20261019/restored-cod-ordinary-orders.png',fullPage:true});
    const readback=await admin.evaluate(async()=>{
        const api=(await import('/src/services/api.js')).default;
        const d=await api.get('/admin/orders/5ab9f08d-b87a-4136-b321-bcd07c4411af');
        let pending;
        try { const r=await api.get('/admin/payments/manual-wallet/pending'); pending={status:r.status,body:r.data}; }
        catch(e) { pending={status:e.response?.status,body:e.response?.data}; }
        const user=(await import('/src/store/authStore.js')).useAuthStore.getState().user;
        return {orderStatus:d.status,details:d.data,pending,adminEmail:user?.email};
    });
    const storefront=await page.evaluate(async()=>{
        const api=(await import('/src/services/api/httpClient.ts')).httpClient;
        const c=await api.get('/api/storefront/store-config');
        const m=await api.get('/api/storefront/payment-methods');
        const cart=await api.get('/api/storefront/cart');
        const foreign=[];
        for(const path of ['/api/storefront/cart','/api/storefront/payment-methods']) {
            // Bypass the normal client's canonical-tenant header normalization.
            const r=await fetch(path,{credentials:'include',headers:{'X-Tenant-Id':'11111111-1111-4111-8111-111111111111'}});
            foreign.push({path,status:r.status,body:await r.json()});
        }
        return {configStatus:c.status,config:{tenantId:c.data.tenantId,storeName:c.data.storeName,currency:c.data.currency},methodsStatus:m.status,methods:m.data,cartStatus:cart.status,cart:cart.data,foreign};
    });
    return {readback,storefront,adminText:await admin.locator('main').innerText()};
}

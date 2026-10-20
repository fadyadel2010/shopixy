async page => {
    const result=await page.evaluate(async()=>{
        const s=(await import('/src/store/checkoutStore.ts')).useCheckoutStore.getState();
        const {httpClient}=await import('/src/services/api/httpClient.ts');
        const cart=(await httpClient.get('/api/storefront/cart')).data;
        const body={cartId:cart.cartId,checkoutSessionId:s.checkoutSessionId,warehouseResolutionId:s.warehouseResolutionId,warehouseResolutionVersion:s.warehouseResolutionVersion,
            shippingMethodId:s.shippingMethodId,paymentMethod:'MANUALWALLET',walletType:s.manualWalletType,walletPhoneNumber:s.manualWalletPhoneNumber,proofFileKey:s.manualWalletProofFileKey,
            shippingAddress:{name:s.address.fullName,phone:s.address.phone,email:s.address.email,street:s.address.addressLine,city:s.address.city,state:s.address.area,country:'EG'},customerNotes:s.address.notes,expectedPricing:s.pricing};
        sessionStorage.setItem('mwqa-checkout-body',JSON.stringify(body));
        try {const r=await httpClient.post('/api/storefront/checkout',body);return {status:r.status,data:r.data};}
        catch(e){return {status:e.response?.status||0,data:e.response?.data};}
    });
    await page.screenshot({path:'.ai/evidence/manual-wallet-20261019/stale-checkout-rejected.png',fullPage:true});
    const admin=page.context().pages()[0];
    const restored=await admin.evaluate(async()=>{
        const api=(await import('/src/services/api.js')).default;
        await api.put('/admin/payment-methods/MANUALWALLET/configure',{configuration:{walletNumber:'01000000000'}});
        return (await api.post('/admin/payment-methods/MANUALWALLET/enable',{})).data;
    });
    return {checkout:result,restored:{enabled:restored.isEnabled,valid:restored.isConfigurationValid}};
}

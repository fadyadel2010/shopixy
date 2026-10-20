async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const storefront = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    const row = admin.getByRole('row').filter({hasText:'MANUALWALLET'});
    await row.getByRole('button',{name:'Configure',exact:true}).click();
    await admin.locator('#payment-config-MANUALWALLET-walletNumber').fill('01000000000');
    await admin.locator('label').filter({has:admin.locator('input[type=checkbox]')}).first().click();
    await admin.getByRole('button',{name:'Save',exact:true}).click();
    await admin.locator('#payment-config-MANUALWALLET-walletNumber').waitFor({state:'hidden'});
    await row.getByRole('button',{name:'Enable',exact:true}).click();
    await row.getByRole('button',{name:'Disable',exact:true}).waitFor();
    const mismatch = await admin.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/payment-methods/MANUALWALLET')).data);
    const publicResult = await storefront.evaluate(async () => {
        const api = (await import('/src/services/api/httpClient.ts')).httpClient;
        const r = await api.get('/api/storefront/payment-methods');
        try { await api.get('/api/storefront/payments/manual-wallet/config'); }
        catch(e) { return {body:r.data, headers:r.headers, diagnostic:e.response?.data,status:e.response?.status}; }
        return {body:r.data,headers:r.headers};
    });
    await admin.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/environment-mismatch-before.png',fullPage:true});
    // Preserve the original legacy bit before the new contract stops accepting it.
    await row.getByRole('button',{name:'Configure',exact:true}).click();
    await admin.locator('label').filter({has:admin.locator('input[type=checkbox]')}).first().click();
    await admin.getByRole('button',{name:'Save',exact:true}).click();
    await admin.locator('#payment-config-MANUALWALLET-walletNumber').waitFor({state:'hidden'});
    await row.getByRole('button',{name:'Disable',exact:true}).click();
    await row.getByRole('button',{name:'Enable',exact:true}).waitFor();
    return {mismatch,publicResult,legacyBitRestored:true,syntheticRecipientMustBeCleared:true};
}

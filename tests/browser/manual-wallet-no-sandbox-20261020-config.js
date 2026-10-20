async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const storefront = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    const checks = [];
    const check = (name, passed) => { checks.push({name,passed:!!passed}); if (!passed) throw new Error(name); };
    const read = () => admin.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/payment-methods/MANUALWALLET')).data);
    try {
        await admin.goto('http://localhost:5173/ecommerce/payment-methods');
        const row = admin.getByRole('row').filter({hasText:'MANUALWALLET'});
        await row.getByRole('button',{name:'Configure',exact:true}).click();
        const input = admin.locator('#payment-config-MANUALWALLET-walletNumber');
        check('No Sandbox or test/live instructions', !/sandbox|test credentials|live mode/i.test(await admin.locator('form').filter({has:input}).innerText()));
        const before = await read();
        for (const invalid of ['', 'bad-wallet', '1234567']) {
            await input.fill(invalid);
            await admin.getByRole('button',{name:'Save',exact:true}).click();
            check('Invalid recipient blocked: '+JSON.stringify(invalid), /valid recipient/.test(await admin.getByRole('alert').innerText()));
        }
        await input.fill('01000000000');
        const requestPromise = admin.waitForRequest(r => r.url().includes('/MANUALWALLET/configure') && r.method()==='PUT');
        await admin.getByRole('button',{name:'Save',exact:true}).click();
        const request = (await requestPromise).postDataJSON();
        await input.waitFor({state:'hidden'});
        check('Save payload contains no mode', !('isSandbox' in request));
        const saved = await read();
        check('Save preserves separate activation', !saved.isEnabled && saved.isConfigurationValid);
        await row.getByRole('button',{name:'Enable',exact:true}).click();
        await row.getByRole('button',{name:'Disable',exact:true}).waitFor();
        const enabled = await read();
        check('Enabled + valid + canonically ready', enabled.isEnabled && enabled.isConfigurationValid && enabled.isAvailableForCheckout);
        check('Mode absent from API', !('isSandbox' in enabled));
        await row.getByRole('button',{name:'Configure',exact:true}).click();
        check('Recipient reopens as saved', await input.inputValue()==='01000000000');
        await admin.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/admin-no-sandbox.png',fullPage:true});
        await input.fill('01000000001');
        await admin.getByRole('button',{name:'Save',exact:true}).click();
        await input.waitFor({state:'hidden'});
        const changed = await read();
        check('Real required setting change reflected immediately', changed.publicConfiguration.walletNumber==='01000000001' && changed.isAvailableForCheckout);
        await row.getByRole('button',{name:'Configure',exact:true}).click();
        await input.fill('01000000000');
        await admin.getByRole('button',{name:'Save',exact:true}).click();
        await input.waitFor({state:'hidden'});
        await admin.reload();
        await row.getByRole('button',{name:'Disable',exact:true}).waitFor();
        const refreshed = await read();
        check('Readiness survives page refresh', refreshed.isAvailableForCheckout && refreshed.isConfigurationValid);
        await admin.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/admin-ready-refreshed.png',fullPage:true});
        const publicResult = await storefront.evaluate(async () => {
            const api = (await import('/src/services/api/httpClient.ts')).httpClient;
            const r = await api.get('/api/storefront/payment-methods');
            const config = await api.get('/api/storefront/payments/manual-wallet/config');
            return {status:r.status,headers:r.headers,body:r.data,config:config.data};
        });
        check('Public COD and wallet available with no private metadata', publicResult.body.paymentMethods.map(m=>m.code).join(',')==='COD,MANUALWALLET' && !JSON.stringify(publicResult.body).includes('walletNumber'));
        return {checks,before,request,saved,enabled,changed,refreshed,publicResult};
    } catch(e) { return {checks,failed:e.message,mustRestore:true}; }
}

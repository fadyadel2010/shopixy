// Actual Admin controls, with independent authoritative API readback. No provider calls.
async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const checks = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const request = (method, path, data, headers) => admin.evaluate(async ({method,path,data,headers}) => {
        try { const r = await (await import('/src/services/api.js')).default.request({method,url:path,data,headers}); return {status:r.status,data:r.data}; }
        catch(e) { return {status:e.response?.status || 0,data:e.response?.data}; }
    }, {method,path,data,headers});
    const wallet = async () => (await request('get','/admin/payment-methods/MANUALWALLET')).data;
    try {
        await admin.goto('http://localhost:5173/ecommerce/payment-methods');
        const row = admin.getByRole('row').filter({hasText:'MANUALWALLET'});
        await row.getByRole('button',{name:'Enable',exact:true}).waitFor();
        const before = await wallet();
        check('Original wallet is disabled, missing recipient; safe to run controlled configuration test', !before.isEnabled && !before.isConfigurationValid);
        check('Direct enable missing persisted recipient rejected', (await request('post','/admin/payment-methods/MANUALWALLET/enable',{})).status === 400);
        await row.getByRole('button',{name:'Enable',exact:true}).click();
        const input = admin.locator('#payment-config-MANUALWALLET-walletNumber');
        await input.waitFor();
        for (const value of ['', '   ', 'not-a-wallet', '1234567', '123456789012345678901']) {
            await input.fill(value);
            await admin.getByRole('button',{name:'Save',exact:true}).click();
            check('UI rejects recipient '+JSON.stringify(value), (await admin.getByRole('alert').innerText()).includes('valid recipient'));
            check('Rejected UI edit retained', await input.inputValue() === value);
            const api = await request('put','/admin/payment-methods/MANUALWALLET/configure',{configuration:{walletNumber:value}});
            check('Backend rejects recipient '+JSON.stringify(value), api.status === 400, {status:api.status});
            const current = await wallet();
            check('Rejected recipient does not activate configuration', !current.isEnabled && !current.isConfigurationValid && current.version === before.version);
        }
        await input.fill('01000000000');
        await admin.getByRole('button',{name:'Save',exact:true}).click();
        await input.waitFor({state:'hidden'});
        await row.getByRole('button',{name:'Enable',exact:true}).click();
        await row.getByRole('button',{name:'Disable',exact:true}).waitFor();
        await admin.reload();
        await row.getByRole('button',{name:'Configure',exact:true}).click();
        check('Saved synthetic public recipient accurately displayed after refresh', await input.inputValue() === '01000000000');
        const enabled = await wallet();
        check('Persisted method enabled and valid', enabled.isEnabled && enabled.isConfigurationValid && enabled.publicConfiguration.walletNumber === '01000000000');
        for (const value of ['', '   ', 'invalid']) {
            await input.fill(value);
            await admin.getByRole('button',{name:'Save',exact:true}).click();
            const failed = await request('put','/admin/payment-methods/MANUALWALLET/configure',{configuration:{walletNumber:value}});
            check('Unsafe active recipient mutation rejected', failed.status === 400);
            const current = await wallet();
            check('Active recipient preserved, not cleared or invalidated', current.isEnabled && current.isConfigurationValid && current.publicConfiguration.walletNumber === '01000000000' && current.version === enabled.version);
        }
        await admin.screenshot({path:'.ai/evidence/manual-wallet-20261019/config-invalid-edit.png',fullPage:true});
        await admin.getByRole('button',{name:'Cancel',exact:true}).click();
        await row.getByRole('button',{name:'Configure',exact:true}).click();
        await admin.screenshot({path:'.ai/evidence/manual-wallet-20261019/config-saved-recipient.png',fullPage:true});
        await admin.getByRole('button',{name:'Cancel',exact:true}).click();
    } catch(e) { return {checks,failure:e.message}; }
    return {checks, syntheticRecipientActiveForControlledQa:true, mustResetAndDisableAtEnd:true};
}

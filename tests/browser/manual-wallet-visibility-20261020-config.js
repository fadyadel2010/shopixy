async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const checks = [];
    const check = (name, passed) => { checks.push({name, passed: !!passed}); if (!passed) throw new Error(name); };
    const wallet = () => admin.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/payment-methods/MANUALWALLET')).data);
    try {
        await admin.goto('http://localhost:5173/ecommerce/payment-methods');
        const row = admin.getByRole('row').filter({hasText: 'MANUALWALLET'});
        await row.getByRole('button', {name: 'Enable', exact: true}).waitFor();
        const before = await wallet();
        check('Original disabled, empty recipient recorded before QA', !before.isEnabled && !before.isConfigurationValid && !before.publicConfiguration?.walletNumber);
        await row.getByRole('button', {name: 'Configure', exact: true}).click();
        const input = admin.locator('#payment-config-MANUALWALLET-walletNumber');
        for (const invalid of ['', 'invalid-wallet', '1234567']) {
            await input.fill(invalid);
            await admin.getByRole('button', {name: 'Save', exact: true}).click();
            check('Invalid recipient blocked in UI: ' + JSON.stringify(invalid), (await admin.getByRole('alert').innerText()).includes('valid recipient'));
        }
        await input.fill('01000000000');
        await admin.locator('label').filter({has: admin.locator('input[type=checkbox]')}).first().click();
        await admin.getByRole('button', {name: 'Save', exact: true}).click();
        await input.waitFor({state: 'hidden'});
        const saved = await wallet();
        check('Saving config alone does not enable wallet', !saved.isEnabled && saved.isConfigurationValid);
        check('Sandbox off is separate from enabled', saved.isSandbox === false);
        await row.getByRole('button', {name: 'Enable', exact: true}).click();
        await row.getByRole('button', {name: 'Disable', exact: true}).waitFor();
        await row.getByRole('button', {name: 'Configure', exact: true}).click();
        check('Saved synthetic recipient reopens accurately', await input.inputValue() === '01000000000');
        const enabled = await wallet();
        check('Authoritative wallet enabled and valid with sandbox off', enabled.isEnabled && enabled.isConfigurationValid && !enabled.isSandbox);
        await admin.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/admin-qa-config.png', fullPage: true});
        await admin.getByRole('button', {name: 'Cancel', exact: true}).click();
        return {checks, before, saved, enabled, mustRestore: true};
    } catch (error) { return {checks, failed: error.message, mustRestore: true}; }
}

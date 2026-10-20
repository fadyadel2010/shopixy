async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const storefront = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    const checks = [];
    const check = (name, passed) => { checks.push({name, passed: !!passed}); if (!passed) throw new Error(name); };
    const wallet = () => admin.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/payment-methods/MANUALWALLET')).data);
    const methods = () => storefront.evaluate(async () => { const r = await (await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/payment-methods'); return {status: r.status, headers: r.headers, body: r.data}; });
    const setMode = async sandbox => {
        await admin.getByRole('row').filter({hasText: 'MANUALWALLET'}).getByRole('button', {name: 'Configure', exact: true}).click();
        if (await admin.locator('input[type=checkbox]').first().isChecked() !== sandbox) {
            await admin.locator('label').filter({has: admin.locator('input[type=checkbox]')}).first().click();
        }
        await admin.getByRole('button', {name: 'Save', exact: true}).click();
        await admin.locator('#payment-config-MANUALWALLET-walletNumber').waitFor({state: 'hidden'});
        await admin.getByRole('row').filter({hasText: 'MANUALWALLET'}).getByRole('button', {name: 'Disable', exact: true}).waitFor();
    };
    try {
        await admin.goto('http://localhost:5173/ecommerce/payment-methods');
        await setMode(false);
        const mismatchAdmin = await wallet();
        const mismatchPublic = await methods();
        check('Enabled valid wallet independently reports environment gate', mismatchAdmin.isEnabled && mismatchAdmin.isConfigurationValid && !mismatchAdmin.isAvailableForCheckout && mismatchAdmin.checkoutUnavailableReasonCode === 'MANUAL_PAYMENT_ENVIRONMENT_MISMATCH');
        check('Actual Admin explains why enabled wallet is unavailable', await admin.getByText(/Sandbox Mode must match the active payment runtime/).isVisible());
        check('Public API suppresses mismatched wallet but retains COD', mismatchPublic.body.paymentMethods.map(m => m.code).join(',') === 'COD');
        await admin.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/admin-runtime-mismatch.png', fullPage: true});
        await setMode(true);
        const alignedAdmin = await wallet();
        const alignedPublic = await methods();
        check('Aligned wallet Admin readiness is true', alignedAdmin.isAvailableForCheckout === true);
        check('Admin displays fresh checkout readiness', await admin.getByText('Available for checkout (order limits still apply)').count() === 2);
        check('Real public API includes COD and wallet', alignedPublic.body.paymentMethods.map(m => m.code).join(',') === 'COD,MANUALWALLET');
        check('Availability response is explicitly non-cacheable', alignedPublic.headers['cache-control'].includes('no-store'));
        await admin.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/admin-runtime-ready.png', fullPage: true});
        return {checks, mismatchAdmin, mismatchPublic, alignedAdmin, alignedPublic};
    } catch (error) { return {checks, failed: error.message}; }
}

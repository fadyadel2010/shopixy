async page => {
    const storefront = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const checks = [];
    const check = (name, passed) => { checks.push({name, passed: !!passed}); if (!passed) throw new Error(name); };
    try {
        await storefront.getByRole('button', {name: 'VF Vodafone Cash', exact: true}).click();
        await storefront.getByPlaceholder('01xxxxxxxxx').fill('01000000097');
        await storefront.locator('input[type=file]').setInputFiles('.ai/evidence/manual-wallet-visibility-20261020/TEST-ONLY-NOT-A-REAL-RECEIPT.png');
        await storefront.getByRole('button', {name: 'Review Order', exact: true}).click();
        await storefront.waitForFunction(() => [...document.querySelectorAll('button')].some(b => b.textContent.includes('Place Order') && !b.disabled));
        const bodySummary = await storefront.evaluate(async () => {
            const loaded = performance.getEntriesByType('resource').find(e => e.name.includes('/src/store/checkoutStore.ts')).name;
            const s = (await import(loaded)).useCheckoutStore.getState();
            const body = {cartId: (await (await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/cart')).data.cartId,
                checkoutSessionId: s.checkoutSessionId, warehouseResolutionId: s.warehouseResolutionId, warehouseResolutionVersion: s.warehouseResolutionVersion,
                shippingMethodId: s.shippingMethodId, paymentMethod: 'MANUALWALLET', walletType: s.manualWalletType, walletPhoneNumber: s.manualWalletPhoneNumber,
                proofFileKey: s.manualWalletProofFileKey, shippingAddress: {name: s.address.fullName, phone: s.address.phone, email: s.address.email, street: s.address.addressLine, city: s.address.city, state: s.address.area, country: 'EG'},
                customerNotes: s.address.notes, expectedPricing: s.pricing};
            sessionStorage.setItem('mwvis-stale-body', JSON.stringify(body));
            return {cartId: body.cartId, checkoutSessionId: body.checkoutSessionId, pricing: body.expectedPricing, hasProof: !!body.proofFileKey};
        });
        check('Stale candidate has real authoritative checkout context', !!bodySummary.checkoutSessionId && bodySummary.pricing?.total === 35 && bodySummary.hasProof);
        await admin.goto('http://localhost:5173/ecommerce/payment-methods');
        const row = admin.getByRole('row').filter({hasText: 'MANUALWALLET'});
        await row.getByRole('button', {name: 'Disable', exact: true}).click();
        await row.getByRole('button', {name: 'Enable', exact: true}).waitFor();
        const disabled = await admin.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/payment-methods/MANUALWALLET')).data);
        check('Admin disable persisted and unready', !disabled.isEnabled && disabled.isAvailableForCheckout === false);
        const staleSubmission = await storefront.evaluate(async () => {
            try { const r = await (await import('/src/services/api/httpClient.ts')).httpClient.post('/api/storefront/checkout', JSON.parse(sessionStorage.getItem('mwvis-stale-body'))); return {status: r.status, body: r.data}; }
            catch (e) { return {status: e.response?.status, body: e.response?.data}; }
        });
        check('Independent server checkout rejects disabled stale candidate', staleSubmission.status === 400 && staleSubmission.body.code === 'INVALID_PAYMENT_METHOD');
        await storefront.getByRole('button', {name: 'Place Order', exact: true}).click();
        await storefront.waitForFunction(async () => {
            const loaded = performance.getEntriesByType('resource').find(e => e.name.includes('/src/store/checkoutStore.ts')).name;
            const s = (await import(loaded)).useCheckoutStore.getState();
            return !s.isSubmitting && s.paymentSelectionStatus === 'stale';
        });
        await storefront.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/stale-wallet-refused.png', fullPage: true});
        await storefront.reload();
        await storefront.getByRole('heading', {name: 'Checkout', exact: true}).waitFor();
        await storefront.getByRole('button', {name: 'Back to Payment', exact: true}).click();
        await storefront.getByText('Cash on Delivery', {exact: true}).waitFor();
        check('Refreshed checkout hides disabled wallet, retains COD', await storefront.locator('input[value=MANUALWALLET]').count() === 0);
        await storefront.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/checkout-wallet-disabled.png', fullPage: true});
        await row.getByRole('button', {name: 'Enable', exact: true}).click();
        await row.getByRole('button', {name: 'Disable', exact: true}).waitFor();
        await storefront.reload();
        await storefront.getByRole('heading', {name: 'Checkout', exact: true}).waitFor();
        if (await storefront.getByRole('button', {name: 'Back to Payment', exact: true}).isVisible()) await storefront.getByRole('button', {name: 'Back to Payment', exact: true}).click();
        await storefront.getByText('P5QA20261017-817 TEST ONLY Wallet', {exact: true}).waitFor();
        check('Re-enabled wallet appears again on refreshed checkout', await storefront.locator('input[value=MANUALWALLET]').count() === 1);
        return {checks, bodySummary, disabled, staleSubmission};
    } catch (error) { return {checks, failed: error.message}; }
}

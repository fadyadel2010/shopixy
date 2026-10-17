async page => {
    const prefix = 'P5QA20261017-817';
    const checks = [], failures = [], requests = [], errors = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const response = r => { if (r.url().includes('/api/')) requests.push({ path: r.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0], method: r.request().method(), status: r.status() }); };
    const error = e => errors.push(e.message.split('\n')[0]);
    page.on('response', response); page.on('pageerror', error);
    const api = (method, path, data, version) => page.evaluate(async ({ method, path, data, version }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data, headers: version ? { 'If-Match': String(version) } : {} }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code || e.response?.data?.error, message: e.response?.data?.message } }; }
    }, { method, path, data, version });
    const read = async path => { const r = await api('get', path); if (r.status !== 200) throw new Error('Read ' + path + ' ' + r.status); return r.data; };
    const publicPayments = async () => {
        const r = await page.request.get('http://localhost:5174/api/storefront/payment-methods', { headers: { Host: 'ahramstore.localhost:5174' } });
        return { status: r.status(), data: await r.json() };
    };
    let original, shippingMethodId;
    try {
        await page.goto('http://localhost:5173/ecommerce/payment-methods');
        await page.getByText('COD', { exact: true }).waitFor();
        original = await read('/admin/payment-methods/COD');
        check('Starting COD identity/enabled state captured', original.isEnabled && original.version > 0, { id: original.id, version: original.version, fee: original.fee, name: original.displayName });
        const codRow = page.getByRole('row').filter({ has: page.getByText('COD', { exact: true }) });
        await codRow.getByRole('button', { name: 'Edit', exact: true }).click();
        await page.getByLabel('Display Name (EN)').fill(prefix + ' COD');
        await page.getByRole('button', { name: 'Update', exact: true }).click();
        await page.getByRole('dialog').waitFor({ state: 'hidden' });
        let current = await read('/admin/payment-methods/COD');
        check('Actual editor save/readback persisted synthetic name', current.displayName === prefix + ' COD' && current.version > original.version);
        const stale = await api('put', '/admin/payment-methods/COD', { ...original, displayName: 'STALE' }, original.version);
        check('Reviewed stale payment form rejected', stale.status === 409, { status: stale.status });
        await codRow.getByRole('button', { name: 'Disable', exact: true }).click();
        await codRow.getByRole('button', { name: 'Enable', exact: true }).waitFor();
        current = await read('/admin/payment-methods/COD');
        const disabled = await publicPayments();
        check('Disabled COD denied by actual public consumer', !current.isEnabled && disabled.status === 200 && !disabled.data.paymentMethods.some(m => m.code === 'COD'), { enabled: current.isEnabled, publicStatus: disabled.status, publicCodes: disabled.data.paymentMethods?.map(m => m.code) });
        await codRow.getByRole('button', { name: 'Enable', exact: true }).click();
        await codRow.getByRole('button', { name: 'Disable', exact: true }).waitFor();
        check('Re-enabled COD read back by public consumer', (await publicPayments()).data.paymentMethods.some(m => m.code === 'COD'));
        const negative = await api('put', '/admin/payment-methods/COD', { fee: -1 }, (await read('/admin/payment-methods/COD')).version);
        check('Hosted server rejects negative payment fee', negative.status === 400, { status: negative.status });
        const walletBefore = await api('get', '/admin/payment-methods/MANUALWALLET');
        check('Wallet is a new synthetic configuration, not an existing credential overwrite', walletBefore.status === 404);
        const walletRow = page.getByRole('row').filter({ has: page.getByText('MANUALWALLET', { exact: true }) });
        await walletRow.getByRole('button', { name: 'Configure', exact: true }).click();
        await page.getByLabel('Wallet Number', { exact: true }).fill('01000000000');
        await page.getByRole('button', { name: 'Save', exact: true }).click();
        await page.getByLabel('Wallet Number', { exact: true }).waitFor({ state: 'hidden' });
        let wallet = await read('/admin/payment-methods/MANUALWALLET');
        check('Synthetic internal wallet configuration saved without secret response', wallet.isConfigurationValid && !wallet.isEnabled && !('configuration' in wallet) && !('configEncrypted' in wallet), { id: wallet.id });
        const named = await api('put', '/admin/payment-methods/MANUALWALLET', { ...wallet, displayName: prefix + ' TEST ONLY Wallet', displayNameAr: 'محفظة اختبار فقط' }, wallet.version);
        check('QA wallet clearly labeled before temporary enable', named.status === 200);
        await page.reload();
        await walletRow.getByRole('button', { name: 'Enable', exact: true }).click();
        await walletRow.getByRole('button', { name: 'Disable', exact: true }).waitFor();
        check('Configured internal Manual payment queue now loads', (await api('get', '/admin/payments/manual-wallet/pending')).status === 200);
        await page.goto('http://localhost:5173/ecommerce/shipping-methods');
        await page.getByRole('button', { name: /Add Shipping Method/ }).waitFor();
        const existing = await read('/shippingmethods?activeOnly=false');
        check('Unique synthetic shipping fixture absent before create', !existing.some(m => m.name.startsWith(prefix)));
        await page.getByRole('button', { name: /Add Shipping Method/ }).click();
        const dialog = page.getByRole('dialog');
        await dialog.getByRole('textbox', { name: 'Name (English)' }).fill(prefix + ' Delivery');
        await dialog.getByRole('spinbutton', { name: 'Base Rate' }).fill('7.35');
        const numbers = dialog.locator('input[type=number]');
        await numbers.nth(1).fill('2.25'); await numbers.nth(2).fill('1000');
        await page.locator('#shipping-min-days').fill('0'); await page.locator('#shipping-max-days').fill('0');
        await dialog.getByRole('combobox', { name: 'Country scope' }).selectOption('selected');
        await dialog.getByRole('listbox', { name: 'Select shipping countries' }).selectOption('EG');
        await dialog.getByRole('button', { name: 'Save', exact: true }).click();
        await dialog.waitFor({ state: 'hidden' });
        const method = (await read('/shippingmethods?activeOnly=false')).find(m => m.name === prefix + ' Delivery');
        shippingMethodId = method?.id;
        check('Actual shipping editor persisted scoped method, zero estimates, price and warehouse', !!method && method.baseRate === 7.35 && method.perKgRate === 2.25 && method.estimatedDeliveryDaysMin === 0 && method.estimatedDeliveryDaysMax === 0 && method.availableCountries === '["EG"]' && !!method.fulfillmentWarehouseId, { shippingMethodId, revision: method?.revision });
        const invalid = await api('post', '/shippingmethods', { ...method, name: prefix + ' invalid', baseRate: -1 });
        check('Negative shipping price rejected server-side', invalid.status === 400);
        const carrier = await api('post', '/shippingmethods', { ...method, name: prefix + ' denied Bosta', carrierCode: 'bosta', isActive: true });
        check('Disabled Bosta not activated through method API', carrier.status === 400, { status: carrier.status, message: carrier.data.message });
        await page.reload();
        await page.getByText(prefix + ' Delivery', { exact: true }).waitFor();
        check('Shipping method survives actual reload', true);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally {
        if (original) {
            const current = await api('get', '/admin/payment-methods/COD');
            if (current.status === 200 && current.data.displayName === prefix + ' COD') {
                if (!current.data.isEnabled) await api('post', '/admin/payment-methods/COD/enable', {}, current.data.version);
                const fresh = await read('/admin/payment-methods/COD');
                const restored = await api('put', '/admin/payment-methods/COD', original, fresh.version);
                checks.push({ name: 'COD original metadata restored with current revision', passed: restored.status === 200 && (await read('/admin/payment-methods/COD')).displayName === original.displayName });
            }
        }
        page.off('response', response); page.off('pageerror', error);
    }
    return { prefix, shippingMethodId, checks, failures, requests, errors };
}

async page => {
    const checks = [], failures = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const api = (method, path, data, version) => page.evaluate(async ({ method, path, data, version }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data, headers: version ? { 'If-Match': String(version) } : {} }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data, version });
    try {
        await page.goto('http://localhost:5173/ecommerce/payment-methods');
        const wallet = await api('get', '/admin/payment-methods/MANUALWALLET');
        check('Cleanup targets only created test-only wallet', wallet.data.id === '8596894a-df67-44c5-8e0e-f64372bc1d74' && wallet.data.displayName === 'P5QA20261017-817 TEST ONLY Wallet');
        const row = page.getByRole('row').filter({ has: page.getByText('MANUALWALLET', { exact: true }) });
        await row.getByRole('button', { name: 'Configure', exact: true }).click();
        await page.getByRole('checkbox', { name: /Reset the stored provider configuration/i }).waitFor();
        const checkboxes = page.locator('form input[type=checkbox]');
        await checkboxes.nth(1).check(); await checkboxes.nth(2).check();
        await page.getByRole('button', { name: /Reset Configuration/i }).click();
        await row.getByRole('button', { name: 'Enable', exact: true }).waitFor();
        const cleared = await api('get', '/admin/payment-methods/MANUALWALLET');
        check('Actual UI reset disables synthetic wallet and invalidates stored configuration', cleared.status === 200 && !cleared.data.isEnabled && !cleared.data.isConfigurationValid);
        const method = await api('get', '/shippingmethods/3953e808-a29b-4003-ba7c-ab74827125fc');
        check('Task-owned shipping method captured inactive before archive', method.status === 200 && method.data.name === 'P5QA20261017-817 Delivery' && !method.data.isActive);
        await page.goto('http://localhost:5173/ecommerce/shipping-methods');
        const shippingRow = page.getByRole('row').filter({ has: page.getByText('P5QA20261017-817 Delivery', { exact: true }) });
        await shippingRow.getByRole('button', { name: 'Delete', exact: true }).click();
        await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click();
        await shippingRow.waitFor({ state: 'hidden' });
        check('Supported shipping archive returns fresh 404', (await api('get', '/shippingmethods/3953e808-a29b-4003-ba7c-ab74827125fc')).status === 404);
        const cod = await api('get', '/admin/payment-methods/COD');
        check('Original COD remains enabled with restored metadata', cod.data.isEnabled && cod.data.displayName === 'Cash on Delivery' && cod.data.fee === 0);
        const account = await api('get', '/admin/whatsapp/accounts/138379f5-7754-4c20-8863-b3b7aad28888');
        check('Phase 4 WhatsApp gate remains untouched and disabled', account.status === 200 && !account.data.enabled);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    return { checks, failures };
}

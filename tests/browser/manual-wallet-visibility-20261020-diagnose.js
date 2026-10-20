async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const storefront = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    const read = () => storefront.evaluate(async () => {
        const api = (await import('/src/services/api/httpClient.ts')).httpClient;
        const get = async path => { try { const r = await api.get(path); return {status: r.status, body: r.data}; } catch (e) { return {status: e.response?.status, body: e.response?.data}; } };
        return {methods: await get('/api/storefront/payment-methods'), wallet: await get('/api/storefront/payments/manual-wallet/config')};
    });
    await admin.getByRole('row').filter({hasText: 'MANUALWALLET'}).getByRole('button', {name: 'Configure', exact: true}).click();
    if (await admin.locator('input[type=checkbox]').first().isChecked()) {
        await admin.locator('label').filter({has: admin.locator('input[type=checkbox]')}).first().click();
    }
    await admin.getByRole('button', {name: 'Save', exact: true}).click();
    await admin.locator('#payment-config-MANUALWALLET-walletNumber').waitFor({state: 'hidden'});
    const mismatch = await read();
    await admin.getByRole('row').filter({hasText: 'MANUALWALLET'}).getByRole('button', {name: 'Configure', exact: true}).click();
    await admin.locator('label').filter({has: admin.locator('input[type=checkbox]')}).first().click();
    await admin.getByRole('button', {name: 'Save', exact: true}).click();
    await admin.locator('#payment-config-MANUALWALLET-walletNumber').waitFor({state: 'hidden'});
    const aligned = await read();
    return {mismatch, aligned};
}

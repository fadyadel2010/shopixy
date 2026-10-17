async page => {
    const checks = [], failures = [], errors = [];
    const prefix = 'P4QAADMIN' + Date.now();
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const errorHandler = e => errors.push(e.message);
    page.on('pageerror', errorHandler);
    const api = (method, path, data) => page.evaluate(async ({ method, path, data }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data });
    let settingsBefore, templateId, phoneBlocked = false, storefront;
    try {
        await page.goto('http://localhost:5173/ecommerce/cart-recovery');
        await page.locator('#first-delay').waitFor();
        settingsBefore = (await api('get', '/admin/ecommerce/abandoned-cart-recovery/settings')).data;
        check('Automation remains disabled while configuration is tested', !settingsBefore.enableAbandonedRecovery);
        const candidate = settingsBefore.firstMessageDelayMinutes === 120 ? 60 : 120;
        await page.locator('#first-delay').selectOption(String(candidate));
        await page.getByRole('button', { name: 'Save Settings', exact: true }).click();
        await page.waitForTimeout(900);
        const saved = await api('get', '/admin/ecommerce/abandoned-cart-recovery/settings');
        check('Actual recovery-settings UI save has authoritative readback', saved.status === 200 && saved.data.firstMessageDelayMinutes === candidate && !saved.data.enableAbandonedRecovery);
        const restore = await api('put', '/admin/ecommerce/abandoned-cart-recovery/settings', settingsBefore);
        check('Only owned recovery configuration restored', restore.status === 200 && (await api('get', '/admin/ecommerce/abandoned-cart-recovery/settings')).data.firstMessageDelayMinutes === settingsBefore.firstMessageDelayMinutes);
        settingsBefore = null;

        const invalid = await api('post', '/admin/phones/block', { phoneNumber: '123', reason: prefix });
        check('Activated backend refuses malformed phone identities', invalid.status === 400, { status: invalid.status });
        const blocked = await api('post', '/admin/phones/block', { phoneNumber: '+20 (100) 000-0099', reason: prefix });
        phoneBlocked = blocked.status === 200;
        check('Canonical synthetic phone block persists', phoneBlocked && blocked.data.isBlocked && blocked.data.phoneNumber === '+201000000099');
        storefront = await page.context().newPage();
        await storefront.goto('http://ahramstore.localhost:5174/'); await storefront.locator('main').waitFor();
        for (const phone of ['01000000099', '+201000000099', '00201000000099', '+20 (100) 000-0099']) {
            const r = await storefront.evaluate(async ({ phone, prefix }) => {
                const { httpClient } = await import('/src/services/api/httpClient.ts');
                try { const r = await httpClient.post('/api/storefront/auth/register', { email: prefix.toLowerCase() + '@example.test', password: 'Aa1!' + crypto.randomUUID(), displayName: prefix, phone }); return { status: r.status }; }
                catch (e) { return { status: e.response?.status, code: e.response?.data?.code || e.response?.data?.error }; }
            }, { phone, prefix });
            if (r.status === 429) check('Additional registration attempts are rate-limited before mutation', r.code === 'RATE_LIMITED', r);
            else check('Equivalent phone registration blocked before account creation or email', [400, 403].includes(r.status) && r.code === 'PHONE_BLOCKED', r);
        }
        await page.goto('http://localhost:5173/ecommerce/blocked-phones');
        await page.getByRole('textbox', { name: /Search by phone or reason/ }).fill(prefix);
        await page.getByText(prefix, { exact: true }).waitFor();
        await page.getByRole('button', { name: 'Unblock', exact: true }).click();
        const dialog = page.getByRole('dialog'); await dialog.waitFor();
        await dialog.getByRole('button', { name: 'Unblock', exact: true }).click();
        await page.getByText(prefix, { exact: true }).waitFor({ state: 'detached' });
        const unblocked = await api('get', '/admin/phones?search=' + prefix);
        check('Actual unblock UI removes only this blocked fixture on fresh readback', unblocked.status === 200 && unblocked.data.totalCount === 0);
        phoneBlocked = false;

        await page.goto('http://localhost:5173/ecommerce/whatsapp');
        await page.getByRole('button', { name: 'Templates', exact: true }).click();
        const templates = (await api('get', '/admin/whatsapp/templates')).data;
        const defaultOrder = templates.find(x => x.isDefault && x.type === 'OrderConfirmation' && x.language === 'en');
        check('Template fixture will not overwrite an existing override', !!defaultOrder);
        await page.getByRole('button', { name: 'Create', exact: true }).first().click();
        const form = page.locator('form').filter({ has: page.locator('#content') });
        await form.locator('#content').fill(prefix + ' {{order_number}} {{total}}');
        await form.locator('#isActive').uncheck();
        await form.getByRole('button', { name: 'Save Template', exact: true }).click();
        await form.waitFor({ state: 'detached' });
        const after = (await api('get', '/admin/whatsapp/templates')).data;
        const fixture = after.find(x => x.content.startsWith(prefix)); templateId = fixture?.id;
        check('Actual WhatsApp template save persists inactive configuration', !!templateId && !fixture.isActive);
        const updated = await api('put', '/admin/whatsapp/templates/' + templateId, { ...fixture, content: prefix + ' edited {{order_number}} {{total}}' });
        check('Template edit has fresh persisted readback', updated.status === 200 && (await api('get', '/admin/whatsapp/templates/' + templateId)).data.content.includes(' edited '));
        const deleted = await api('delete', '/admin/whatsapp/templates/' + templateId);
        check('Template cleanup confirmed by fresh 404', deleted.status === 204 && (await api('get', '/admin/whatsapp/templates/' + templateId)).status === 404);
        templateId = null;
        await page.locator('main').getByRole('button', { name: 'Analytics', exact: true }).click();
        const metrics = await api('get', '/admin/whatsapp/analytics');
        await page.waitForTimeout(500);
        check('Messaging metrics use actual nonempty sent count, not a fabricated total', metrics.status === 200 && metrics.data.totalMessagesSent > 0 && (await page.locator('main').innerText()).includes(String(metrics.data.totalMessagesSent)), { sentMessages: metrics.data.totalMessagesSent, failedMessages: metrics.data.totalMessagesFailed });
        await page.getByRole('button', { name: 'Logs', exact: true }).click();
        await page.waitForTimeout(500);
        check('Actual logs show recipient/type fields from the real DTO', await page.locator('main tbody tr').count() > 0 && !(await page.locator('main tbody').innerText()).includes('undefined'));
        check('No uncaught errors in hosted Admin workflows', errors.length === 0);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally {
        if (phoneBlocked) await api('post', '/admin/phones/unblock', { phoneNumber: '+201000000099' });
        if (templateId) await api('delete', '/admin/whatsapp/templates/' + templateId);
        if (settingsBefore) { const current = await api('get', '/admin/ecommerce/abandoned-cart-recovery/settings'); if (!current.data?.enableAbandonedRecovery) await api('put', '/admin/ecommerce/abandoned-cart-recovery/settings', settingsBefore); }
        if (storefront) await storefront.close();
        page.off('pageerror', errorHandler);
    }
    return { prefix, checks, failures, errors };
}

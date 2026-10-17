// Final hosted read-only verification after all source changes. No commands.
async page => {
    const checks = [], failures = [], errors = [];
    const check = (name, passed, evidence = {}) => { checks.push({ name, passed, ...evidence }); if (!passed) throw new Error(name); };
    const onError = error => errors.push(error.message);
    page.on('pageerror', onError);
    let anonymous;
    const originalLanguage = await page.evaluate(() => localStorage.getItem('i18nextLng'));
    try {
        await page.evaluate(() => localStorage.setItem('i18nextLng', 'en'));
        await page.goto('http://localhost:5173/storefront/overview');
        await page.getByRole('heading', { name: 'Overview', exact: true }).waitFor();
        await page.getByText('AhramStore', { exact: true }).waitFor();
        check('Dashboard compatibility route resolves to the current tenant overview', true);
        const current = await page.evaluate(async () => {
            const config = await (await fetch('/api/admin/store-config')).json();
            const fields = await (await fetch('/api/admin/storefront/checkout-fields')).json();
            const domains = await (await fetch('/api/admin/storefront/domains/dashboard')).json();
            return { config: { tenantId: config.tenantId, status: config.storefrontStatus, rowVersion: config.rowVersion, messageContainsQaMarker: (config.maintenanceMessage || '').includes('QA Phase1') }, fields: { count: fields.fields.length, enabled: fields.fields.filter(f => f.isEnabled).length, qaMarkerPresent: fields.fields.some(f => f.label.startsWith('QA Phase1')) }, domains: domains.totalDomains };
        });
        check('Final active settings/fields/domain portfolio restored', current.config.status === 'live' && !current.config.messageContainsQaMarker && current.fields.count === 10 && current.fields.enabled === 7 && !current.fields.qaMarkerPresent && current.domains === 0, current);
        const id = '11111111-1111-4111-8111-111111111111';
        for (const suffix of ['', '/verification', '/ssl', '/redirects', '/history']) {
            const requests = [];
            const collect = r => { if (r.url().includes('/api/admin/storefront/domains/')) requests.push({ path: r.url().split('/api/')[1], status: r.status() }); };
            page.on('response', collect);
            await page.goto('http://localhost:5173/storefront/domains/' + id + suffix);
            await page.getByRole('heading', { name: 'Domain not found', exact: true }).waitFor();
            check('Missing-domain surface safely explains unavailable resource: ' + (suffix || 'detail'), requests.some(r => r.status === 404), { requests });
            page.removeListener('response', collect);
        }
        await page.goto('http://localhost:5173/storefront/domains/list');
        await page.getByRole('heading', { name: /No domains/i }).waitFor();
        check('Hosted domain list has a genuine empty state', true);
        await page.goto('http://localhost:5173/storefront/domains/activity');
        await page.getByRole('heading', { name: /No recent activity/i }).waitFor();
        check('Hosted activity has a genuine empty state', true);
        anonymous = await page.context().browser().newContext();
        for (const path of ['admin/store-config', 'admin/wishlist-analytics', 'admin/storefront/checkout-fields', 'admin/storefront/domains/dashboard']) {
            const response = await anonymous.request.get('http://localhost:5010/api/' + path, { headers: { 'X-Tenant-Id': 'c9c5c1ae-949e-498e-bc63-07242daeb49d' } });
            const body = await response.json();
            check('Anonymous cannot use a raw tenant header to read Admin contract: ' + path, response.status() === 400 && body.code === 'TENANT_REQUIRED', { status: response.status(), code: body.code });
        }
        const forged = await page.evaluate(async () => {
            const response = await fetch('/api/admin/storefront/domains/dashboard', { headers: { 'X-Tenant-Id': '11111111-1111-4111-8111-111111111111' } });
            return { status: response.status };
        });
        check('Authenticated tenant-header spoof cannot read a foreign portfolio', forged.status === 403, forged);
        check('No uncaught errors in final hosted read-only pass', errors.length === 0, { errors });
    } catch (error) { failures.push(error.message.split('\n')[0]); }
    finally {
        if (anonymous) await anonymous.close();
        page.removeListener('pageerror', onError);
        await page.evaluate(value => value ? localStorage.setItem('i18nextLng', value) : localStorage.removeItem('i18nextLng'), originalLanguage);
    }
    return { observedUtc: new Date().toISOString(), layer: 'hosted-read-only', checks, failures, errors, mutationRequests: 0 };
}

// Existing authenticated Owner session. No credentials, order submission,
// availability transition, DNS/TLS command, routing activation or external message.
async page => {
    const checks = [], failures = [], pageErrors = [];
    const check = (name, passed, evidence = {}) => {
        checks.push({ name, passed, ...evidence });
        if (!passed) throw new Error(name);
    };
    const onError = error => pageErrors.push(error.message);
    page.on('pageerror', onError);
    const api = (path, method = 'GET', body, version) => page.evaluate(async ({ path, method, body, version }) => {
        const csrf = decodeURIComponent(document.cookie.split(';').map(p => p.trim()).find(p => p.startsWith('csrf_token='))?.slice(11) || '');
        const r = await fetch('/api/' + path, { method, credentials: 'include', headers: {
            'Content-Type': 'application/json', 'Cache-Control': 'no-store',
            ...(method !== 'GET' ? { 'X-CSRF-Token': csrf } : {}),
            ...(version ? { 'If-Match': '"' + version + '"' } : {}),
        }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        let data; try { data = await r.json(); } catch { data = null; }
        return { status: r.status, data };
    }, { path, method, body, version });
    const publicRead = async path => {
        // Node does not apply Chromium's *.localhost loopback resolution.
        const r = await page.request.get('http://localhost:5174/api/storefront/' + path, { headers: { 'Host': 'ahramstore.localhost:5174', 'Cache-Control': 'no-store' } });
        return { status: r.status(), data: await r.json() };
    };
    const key = 'QA Phase1 optional ' + Date.now();
    let baseline, ownedMessage, createdField, expectedField;
    const originalLanguage = await page.evaluate(() => localStorage.getItem('i18nextLng'));
    const originalViewport = page.viewportSize();
    try {
        await page.evaluate(() => localStorage.setItem('i18nextLng', 'en'));
        await page.setViewportSize({ width: 1440, height: 1000 });
        await page.goto('http://localhost:5173/storefront');
        await page.getByRole('heading', { name: 'Overview', exact: true }).waitFor();
        await page.getByText('No wishlist activity yet', { exact: true }).waitFor();
        check('Dashboard hosted empty analytics remains refreshable', await page.getByRole('button', { name: 'Refresh', exact: true }).isVisible());
        const analyticsResponse = page.waitForResponse(r => r.url().includes('/api/admin/wishlist-analytics') && r.request().method() === 'GET');
        await page.getByRole('button', { name: 'Refresh', exact: true }).click();
        const analytics = await (await analyticsResponse).json();
        check('Dashboard totals match independent SQL observation', analytics.totalWishlistItems === 0 && analytics.totalCustomersWithWishlists === 0 && analytics.averageItemsPerWishlist === 0, { items: analytics.totalWishlistItems, customers: analytics.totalCustomersWithWishlists });
        await page.locator('main').getByRole('button', { name: 'Manage →', exact: true }).click();
        await page.waitForURL('**/storefront/status');
        await page.locator('input[value="live"]').waitFor();
        const initial = await api('admin/store-config');
        baseline = initial.data;
        check('Current tenant and live status admitted before the reversible check', initial.status === 200 && baseline.tenantId === 'c9c5c1ae-949e-498e-bc63-07242daeb49d' && baseline.storefrontStatus === 'live', { tenantId: baseline.tenantId, rowVersion: baseline.rowVersion });
        ownedMessage = key + ' · اختبار';
        await page.locator('input[value="maintenance"]').check();
        await page.getByRole('textbox', { name: 'Maintenance Message' }).fill(ownedMessage);
        await page.locator('input[value="live"]').check();
        await page.getByRole('button', { name: 'Save live Status', exact: true }).click();
        const saveResponse = page.waitForResponse(r => r.url().endsWith('/api/admin/store-config/status') && r.request().method() === 'PUT');
        await page.getByRole('dialog').getByRole('button', { name: 'Save live Status', exact: true }).click();
        const accepted = await saveResponse;
        check('Settings UI commits only message while availability remains live', accepted.status() === 200);
        await page.getByText('Storefront Status saved live', { exact: true }).waitFor();
        const readback = await api('admin/store-config');
        check('Fresh Admin readback contains accepted Unicode message', readback.status === 200 && readback.data.maintenanceMessage === ownedMessage && readback.data.storefrontStatus === 'live' && readback.data.rowVersion !== baseline.rowVersion, { rowVersion: readback.data.rowVersion });
        const ignored = new Set(['rowVersion', 'updatedAtUtc', 'maintenanceMessage']);
        const changed = Object.keys(baseline).filter(k => !ignored.has(k) && JSON.stringify(baseline[k]) !== JSON.stringify(readback.data[k]));
        check('Status-only endpoint preserves every unrelated Admin configuration property', changed.length === 0, { changed });
        const consumer = await publicRead('store-config');
        check('Fresh public SQL-backed consumer sees message without closing store', consumer.status === 200 && consumer.data.maintenanceMessage === ownedMessage && consumer.data.storefrontStatus === 'live', { http: consumer.status });
        const stale = await api('admin/store-config/status', 'PUT', { storefrontStatus: 'live', maintenanceMessage: ownedMessage }, baseline.rowVersion);
        check('Hosted status endpoint rejects stale version instead of replacing concurrent data', stale.status === 409, { http: stale.status });

        // Restore only our message, using a fresh token and checking field ownership.
        const beforeRestore = await api('admin/store-config');
        check('Restoration ownership check', beforeRestore.data.maintenanceMessage === ownedMessage && beforeRestore.data.storefrontStatus === baseline.storefrontStatus);
        const restored = await api('admin/store-config/status', 'PUT', { storefrontStatus: beforeRestore.data.storefrontStatus, maintenanceMessage: baseline.maintenanceMessage }, beforeRestore.data.rowVersion);
        check('Narrow conditional status restoration accepted', restored.status === 200);
        ownedMessage = null;
        const restoredConsumer = await publicRead('store-config');
        check('Public consumer verifies original message and availability restored', restoredConsumer.status === 200 && (restoredConsumer.data.maintenanceMessage || '') === (baseline.maintenanceMessage || '') && restoredConsumer.data.storefrontStatus === baseline.storefrontStatus);

        await page.goto('http://localhost:5173/storefront/checkout-fields');
        await page.getByRole('button', { name: 'Add Custom Field', exact: true }).waitFor();
        const beforeFields = await api('admin/storefront/checkout-fields');
        check('Hosted checkout field baseline is 10 existing definitions', beforeFields.status === 200 && beforeFields.data.fields.length === 10);
        await page.getByRole('button', { name: 'Add Custom Field', exact: true }).click();
        await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();
        check('Editor validates an empty label before any create request', await page.getByRole('alert').filter({ hasText: /Label/ }).isVisible());
        await page.locator('#checkout-field-label').fill(key);
        await page.locator('#checkout-field-type').selectOption('SELECT');
        const option = page.getByPlaceholder('Option value');
        await option.pressSequentially('Large');
        check('Real browser option typing retains focus and all characters', await option.inputValue() === 'Large' && await option.evaluate(el => document.activeElement === el));
        await page.getByRole('checkbox', { name: /Show in Checkout/ }).uncheck();
        const createResponse = page.waitForResponse(r => r.url().endsWith('/api/admin/storefront/checkout-fields') && r.request().method() === 'POST');
        await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();
        const created = await createResponse;
        createdField = await created.json();
        expectedField = createdField;
        check('Scoped disabled optional field creation persists successfully', created.status() === 201 && createdField.id && !createdField.isRequired && !createdField.isEnabled, { id: createdField.id, http: created.status() });
        const disabledConsumer = await publicRead('checkout-fields');
        check('Runtime excludes disabled custom field', disabledConsumer.status === 200 && !disabledConsumer.data.fields.some(f => f.id === createdField.id));
        await page.getByText(key, { exact: true }).waitFor();
        await page.locator('li').filter({ has: page.getByText(key, { exact: true }) }).getByRole('button', { name: 'Edit', exact: true }).click();
        await page.getByRole('checkbox', { name: /Show in Checkout/ }).check();
        const updateResponse = page.waitForResponse(r => r.url().endsWith('/' + createdField.id) && r.request().method() === 'PATCH');
        await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();
        const updated = await updateResponse;
        expectedField = await updated.json();
        const enabledConsumer = await publicRead('checkout-fields');
        const visible = enabledConsumer.data.fields.find(f => f.id === createdField.id);
        check('Enabled optional field and options propagate to public runtime', updated.status() === 200 && visible && visible.label === key && !visible.isRequired && visible.configuration.options[0].value === 'Large', { http: enabledConsumer.status });
        await page.getByRole('dialog').waitFor({ state: 'hidden' });
        await page.locator('li').filter({ has: page.getByText(key, { exact: true }) }).getByRole('button', { name: 'Archive', exact: true }).click();
        const archiveResponse = page.waitForResponse(r => r.url().endsWith('/' + createdField.id) && r.request().method() === 'DELETE');
        await page.getByRole('dialog').getByRole('button', { name: 'Archive', exact: true }).click();
        check('Scoped custom field archived through actual UI', (await archiveResponse).status() === 204);
        createdField = null;
        const afterFields = await api('admin/storefront/checkout-fields');
        check('Original checkout definitions and their ordering remain byte-for-byte equal', JSON.stringify(afterFields.data.fields) === JSON.stringify(beforeFields.data.fields));
        const afterConsumer = await publicRead('checkout-fields');
        check('Archived synthetic field absent from runtime', !afterConsumer.data.fields.some(f => f.label === key));

        await page.goto('http://localhost:5173/storefront/domains');
        await page.getByRole('heading', { name: /No domains/i }).waitFor();
        const domains = await api('admin/storefront/domains/dashboard');
        check('Domain empty state matches real tenant portfolio rather than claiming connectivity', domains.status === 200 && domains.data.totalDomains === 0, { totalDomains: domains.data.totalDomains });
        await page.goto('http://localhost:5173/storefront/domains/add');
        await page.getByRole('textbox', { name: 'Hostname', exact: true }).fill('qa-phase1.example.test');
        for (let i = 1; i < 7; i++) await page.getByRole('button', { name: 'Next', exact: true }).click();
        check('Real domain wizard reaches review without any create or verification command', await page.getByRole('button', { name: 'Create Domain', exact: true }).isVisible());
        await page.getByRole('button', { name: 'Back', exact: true }).click();
        await page.getByRole('dialog').getByRole('button', { name: /Discard/i }).click();
        await page.waitForURL('**/storefront/domains');
        const negative = await api('admin/storefront/domains', 'POST', { hostName: 'http://invalid host/path', domainType: 1, redirectType: 0, canonicalPolicy: 0 });
        check('Hosted domain API rejects malformed host without creating a domain', negative.status === 400, { http: negative.status });
        const foreign = await api('admin/storefront/domains/11111111-1111-4111-8111-111111111111');
        check('Hosted unknown tenant-scoped domain does not leak details', foreign.status === 404, { http: foreign.status });
        check('No domain persisted during wizard and negative checks', (await api('admin/storefront/domains/dashboard')).data.totalDomains === 0);

        // Arabic/RTL and narrow-screen rendering, no backend mutation.
        await page.evaluate(() => localStorage.setItem('i18nextLng', 'ar'));
        await page.setViewportSize({ width: 390, height: 844 });
        await page.goto('http://localhost:5173/storefront/status');
        await page.locator('input[value="live"]').waitFor();
        check('Settings Arabic labels render without raw translation keys', !(await page.locator('main').innerText()).includes('storefront.') && await page.evaluate(() => document.documentElement.dir === 'rtl'));
        const size = await page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, width: innerWidth }));
        check('Narrow RTL Settings has no page-wide horizontal overflow', size.scroll <= size.width + 1, size);
        check('No uncaught browser runtime errors', pageErrors.length === 0, { pageErrors });
    } catch (error) {
        failures.push(error.message.split('\n')[0]);
    } finally {
        if (ownedMessage && baseline) {
            try {
                const current = await api('admin/store-config');
                if (current.data?.maintenanceMessage !== ownedMessage || current.data?.storefrontStatus !== baseline.storefrontStatus) throw new Error('Message changed concurrently; automatic restoration withheld.');
                const result = await api('admin/store-config/status', 'PUT', { storefrontStatus: current.data.storefrontStatus, maintenanceMessage: baseline.maintenanceMessage }, current.data.rowVersion);
                check('Finally restored owned message', result.status === 200);
            } catch (error) { failures.push('RESTORATION: ' + error.message); }
        }
        if (createdField) {
            try {
                const current = (await api('admin/storefront/checkout-fields')).data.fields.find(f => f.id === createdField.id);
                if (current && JSON.stringify(current) !== JSON.stringify(expectedField)) throw new Error('Synthetic field changed concurrently; automatic archive withheld.');
                if (current) check('Finally archived owned synthetic field', (await api('admin/storefront/checkout-fields/' + createdField.id, 'DELETE')).status === 204);
            } catch (error) { failures.push('FIELD CLEANUP: ' + error.message); }
        }
        await page.evaluate(value => value ? localStorage.setItem('i18nextLng', value) : localStorage.removeItem('i18nextLng'), originalLanguage);
        if (originalViewport) await page.setViewportSize(originalViewport);
        page.removeListener('pageerror', onError);
    }
    return { observedUtc: new Date().toISOString(), checks, failures, pageErrors, boundaries: { orderSubmitted: false, availabilityChanged: false, domainCreated: false, dnsOrTlsCommand: false } };
}

// Actual source components in fresh browser contexts. ALL API calls below are
// intercepted; this is controlled-contract evidence, never external execution.
async page => {
    const checks = [], failures = [], errors = [], moduleFailures = [];
    const check = (name, passed) => { checks.push({ name, passed }); if (!passed) throw new Error(name); };
    const dependency = (source, path) => source.match(new RegExp('"(' + path.replaceAll('.', '\\.') + '[^" ]*)"'))[1];
    const contexts = [];
    const browser = page.context().browser();
    const tenantId = '22222222-2222-4222-8222-222222222222';
    const config = { tenantId, storeName: 'Synthetic Store', themeId: 'default', storefrontStatus: 'live', maintenanceMessage: '', rowVersion: '1' };
    const field = { id: '33333333-3333-4333-8333-333333333333', fieldKind: 'SYSTEM', fieldType: 'TEXT', systemFieldKey: 'full_name', label: 'Full Name', isEnabled: true, isRequired: true, displayOrder: 1, configuration: { schemaVersion: 1 } };
    const fixture = route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><body><div id="fixture"></div></body></html>' });
    let configFails = true, wishlistFails = true, domainFails = true, fieldReadFails = false, creates = 0, domainCreates = 0, verificationStarts = 0;
    try {
        const admin = await browser.newContext(); contexts.push(admin);
        const tab = await admin.newPage();
        tab.on('pageerror', error => errors.push(error.message));
        await tab.route('**/__phase1__', fixture);
        await tab.route('**/api/**', route => {
            const path = route.request().url().replace(/^https?:\/\/[^/]+/, '').split('?')[0];
            if (!path.startsWith('/api/')) return route.continue();
            const method = route.request().method();
            const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
            if (path === '/api/settings') return json({ storefrontUrl: 'https://synthetic.example.test' });
            if (path === '/api/admin/store-config') return json(configFails ? { message: 'Controlled config failure' } : config, configFails ? 503 : 200);
            if (path === '/api/admin/wishlist-analytics') return json(wishlistFails ? { message: 'Controlled analytics failure' } : { totalWishlistItems: 0, totalCustomersWithWishlists: 0, averageItemsPerWishlist: 0, topProducts: [], recentActivity: { itemsAddedLast7Days: 0 } }, wishlistFails ? 503 : 200);
            if (path === '/api/admin/storefront/domains/dashboard') return json(domainFails ? { message: 'Controlled domain query failure' } : { totalDomains: 0 }, domainFails ? 503 : 200);
            if (path === '/api/admin/storefront/checkout-fields' && method === 'GET') return json(fieldReadFails ? { message: 'Controlled readback failure' } : { fields: [field] }, fieldReadFails ? 503 : 200);
            if (path === '/api/admin/storefront/checkout-fields' && method === 'POST') {
                creates++; fieldReadFails = true;
                return json({ ...route.request().postDataJSON(), id: '44444444-4444-4444-8444-444444444444', fieldKind: 'CUSTOM' }, 201);
            }
            if (path === '/api/admin/storefront/domains' && method === 'POST') { domainCreates++; return json({ domainId: '55555555-5555-4555-8555-555555555555', version: 1 }, 201); }
            if (path.endsWith('/verification/start')) { verificationStarts++; return json(verificationStarts === 1 ? { message: 'Controlled setup failure' } : { version: 2 }, verificationStarts === 1 ? 503 : 200); }
            return json({});
        });
        await tab.goto('http://localhost:5173/__phase1__');
        await tab.evaluate(async ({ dependencySource, tenantId }) => {
            const dep = eval('(' + dependencySource + ')');
            const refresh = await import('/@react-refresh'); refresh.injectIntoGlobalHook(window);
            window.$RefreshReg$ = () => {}; window.$RefreshSig$ = () => type => type;
            const React = (await import('/node_modules/.vite/deps/react.js')).default;
            const { createRoot } = (await import('/node_modules/.vite/deps/react-dom_client.js')).default;
            const source = await (await fetch('/src/pages/StorefrontSettings.jsx')).text();
            const { default: Settings } = await import('/src/pages/StorefrontSettings.jsx');
            const { default: Status } = await import('/src/components/StorefrontStatusCard.jsx');
            const { default: Wishlist } = await import('/src/components/WishlistAnalyticsCard.jsx');
            const { CheckoutFieldsPage: Fields } = await import('/src/modules/storefront/checkoutFields/pages/CheckoutFieldsPage.jsx');
            const { AddDomainWizardPage: Wizard } = await import('/src/modules/storefront/domains/pages/AddDomainWizardPage.jsx');
            const { DomainsOverviewPage: Domains } = await import('/src/modules/storefront/domains/pages/DomainsOverviewPage.jsx');
            const { SettingsProvider } = await import(dep(source, '/src/contexts/SettingsContext.jsx'));
            const { ToastProvider } = await import(dep(source, '/src/components/Toast.jsx'));
            const { useAuthStore } = await import(dep(source, '/src/store/authStore.js'));
            const { MemoryRouter } = await import((await (await fetch('/src/components/StorefrontStatusCard.jsx')).text()).match(/from "([^"]*react-router-dom[^"]*)"/)[1]);
            const i18n = (await import('/src/i18n/config.js')).default; await i18n.changeLanguage('en');
            useAuthStore.setState({ user: { id: '11111111-1111-4111-8111-111111111111', role: 'Owner', tenantId }, isAuthenticated: true, hasHydrated: true });
            const components = { Settings, Status, Wishlist, Fields, Wizard, Domains };
            const root = createRoot(document.getElementById('fixture')); let key = 0;
            window.phase1Mount = name => root.render(React.createElement(MemoryRouter, { key: ++key }, React.createElement(SettingsProvider, null, React.createElement(ToastProvider, null, React.createElement(components[name])))));
        }, { dependencySource: dependency.toString(), tenantId });
        await tab.evaluate(() => window.phase1Mount('Status'));
        await tab.getByText('Unable to load storefront status', { exact: true }).waitFor();
        check('Failed status read is unavailable, never falsely Not configured', await tab.getByText('Not configured', { exact: true }).count() === 0);
        configFails = false;
        await tab.getByRole('button', { name: 'Retry', exact: true }).click();
        await tab.getByText('Synthetic Store', { exact: true }).waitFor();
        check('Status error retries successfully', true);

        await tab.evaluate(() => window.phase1Mount('Wishlist'));
        await tab.getByText('Unable to load wishlist analytics', { exact: true }).waitFor();
        wishlistFails = false;
        await tab.getByRole('button', { name: 'Retry', exact: true }).click();
        await tab.getByText('No wishlist activity yet', { exact: true }).waitFor();
        check('Analytics failure is visible and recovers to refreshable empty state', await tab.getByRole('button', { name: 'Refresh', exact: true }).isVisible());

        configFails = true;
        await tab.evaluate(() => window.phase1Mount('Settings'));
        await tab.getByRole('button', { name: 'Retry', exact: true }).waitFor();
        configFails = false;
        await tab.getByRole('button', { name: 'Retry', exact: true }).click();
        await tab.locator('input[value="live"]').waitFor();
        check('Settings initial failure has an effective retry', true);

        await tab.evaluate(() => window.phase1Mount('Domains'));
        await tab.getByText('Unable to load domains', { exact: true }).waitFor();
        check('Domain unavailable query does not become an empty portfolio', await tab.getByRole('heading', { name: /No domains/i }).count() === 0);
        domainFails = false;
        await tab.getByRole('button', { name: 'Retry', exact: true }).click();
        await tab.getByRole('heading', { name: /No domains/i }).waitFor();
        check('Domain retry admits a genuine empty portfolio', true);

        await tab.evaluate(() => window.phase1Mount('Fields'));
        await tab.getByRole('button', { name: 'Add Custom Field', exact: true }).click();
        await tab.locator('#checkout-field-label').fill('Synthetic readback test');
        await tab.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();
        await tab.getByText(/saved, but the list could not be reloaded/).waitFor();
        check('Committed create/readback failure closes editor without misleading success or duplicate create', creates === 1 && await tab.getByRole('dialog').count() === 0 && await tab.getByText('Custom field created.', { exact: true }).count() === 0);

        await tab.evaluate(() => window.phase1Mount('Wizard'));
        await tab.getByRole('textbox', { name: 'Hostname', exact: true }).fill('phase1.example.test');
        for (let step = 1; step < 7; step++) await tab.getByRole('button', { name: 'Next', exact: true }).click();
        await tab.getByRole('button', { name: 'Create Domain', exact: true }).click();
        await tab.getByText(/domain was saved, but verification/).waitFor();
        await tab.getByRole('button', { name: 'Retry verification setup', exact: true }).click();
        await tab.waitForURL('**/storefront/domains/55555555-5555-4555-8555-555555555555/verification');
        check('Wizard partial success retries only verification and navigates after releasing busy guard', domainCreates === 1 && verificationStarts === 2);

        const storefront = await browser.newContext(); contexts.push(storefront);
        const shipping = await storefront.newPage(); shipping.on('pageerror', error => errors.push(error.message));
        shipping.on('requestfailed', request => { if (request.url().includes('/src/')) moduleFailures.push({ url: request.url(), error: request.failure()?.errorText }); });
        shipping.on('response', response => { if (response.status() >= 400 && response.url().includes('/src/')) moduleFailures.push({ url: response.url(), status: response.status() }); });
        await shipping.route('**/__phase1__', fixture);
        await shipping.route('**/api/**', route => {
            const path = route.request().url().replace(/^https?:\/\/[^/]+/, '').split('?')[0];
            return path.startsWith('/api/') ? route.fulfill({ contentType: 'application/json', body: '{}' }) : route.continue();
        });
        await shipping.goto('http://localhost:5174/__phase1__');
        await shipping.evaluate(async ({ dependencySource, tenantId }) => {
            const dep = eval('(' + dependencySource + ')');
            const refresh = await import('/@react-refresh'); refresh.injectIntoGlobalHook(window);
            window.$RefreshReg$ = () => {}; window.$RefreshSig$ = () => type => type;
            const React = (await import('/node_modules/.vite/deps/react.js')).default;
            const { createRoot } = (await import('/node_modules/.vite/deps/react-dom_client.js')).default;
            const source = await (await fetch('/src/components/checkout/ShippingStep.tsx')).text();
            const { ShippingStep } = await import('/src/components/checkout/ShippingStep.tsx');
            const { useCheckoutStore } = await import(dep(source, '/src/store/checkoutStore.ts'));
            const { useStoreConfigStore } = await import(dep(source, '/src/store/storeConfigStore.ts'));
            const { setResolvedTenant } = await import('/src/bootstrap/tenantResolver.ts'); setResolvedTenant(tenantId);
            const { useLanguageStore } = await import('/src/i18n/languageStore.ts'); useLanguageStore.getState().setLanguage('en');
            const id = '66666666-6666-4666-8666-666666666666';
            useStoreConfigStore.setState(s => ({ config: { ...s.config, currency: 'EGP', defaultCountryCode: 'EG' }, isLoaded: true }));
            useCheckoutStore.setState({ checkoutFields: [{ id, fieldKind: 'CUSTOM', fieldType: 'SELECT', label: 'Synthetic required choice', isRequired: true, isEnabled: true, displayOrder: 1, configuration: { schemaVersion: 1, options: [{ value: 'one', label: 'One' }] } }], checkoutFieldsStatus: 'AVAILABLE', checkoutFieldsLoading: false, checkoutFieldsTotalCount: 1, shippingMethods: [], shippingMethodsLoading: false, shippingMethodsStatus: 'EMPTY', customFieldValues: {} });
            window.phase1Checkout = { store: useCheckoutStore, id };
            const root = createRoot(document.getElementById('fixture'));
            root.render(React.createElement(ShippingStep, { onContinue: () => {} }));
        }, { dependencySource: dependency.toString(), tenantId });
        const choice = shipping.getByRole('combobox', { name: /Synthetic required choice/ });
        await choice.waitFor();
        await choice.focus(); await choice.blur();
        check('Runtime renders enabled configured SELECT and its options', await choice.locator('option').allTextContents().then(options => options.includes('One')));
        check('Runtime required validator rejects missing custom value', await shipping.evaluate(() => { const { store, id } = window.phase1Checkout; store.getState().touchCustomField(id); return Boolean(store.getState().getCustomFieldError(id)); }));
        await choice.selectOption('one');
        check('Runtime accepts a configured option without submitting an order', await shipping.evaluate(() => { const { store, id } = window.phase1Checkout; return store.getState().customFieldValues[id] === 'one' && !store.getState().getCustomFieldError(id); }));
        await shipping.evaluate(() => window.phase1Checkout.store.setState({ checkoutFields: [], checkoutFieldsStatus: 'UNAVAILABLE', checkoutFieldsError: 'Controlled field dependency failure' }));
        await shipping.getByText('Controlled field dependency failure', { exact: true }).waitFor();
        check('Unavailable runtime fields fail closed instead of silently using legacy inputs', await shipping.getByRole('button', { name: /retry/i }).count() > 0);
        check('No uncaught errors across controlled component contexts', errors.length === 0);
    } catch (error) { failures.push(error.message.split('\n')[0]); }
    finally { for (const context of contexts) await context.close(); }
    return { layer: 'real-components-controlled-HTTP', checks, failures, errors, moduleFailures, apiIntercepted: true, orderSubmitted: false, actualDnsTlsExecution: false };
}

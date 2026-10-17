// Real components and stores in isolated browser contexts. HTTP is controlled;
// these cases never touch the shared cart, orders, storage or messaging services.
async page => {
    const results = [], errors = [];
    const check = (name, passed) => { results.push({ name, passed }); if (!passed) throw new Error(name); };
    const dependency = (source, path) => source.match(new RegExp('"(' + path.replaceAll('.', '\\.') + '[^" ]*)"'))[1];
    const browser = page.context().browser();
    const storefront = await browser.newContext();
    const admin = await browser.newContext();
    let cartMode = 'valid', attachmentRequests = 0;
    const cart = {
        cartId: '11111111-1111-4111-8111-111111111111', tenantId: '22222222-2222-4222-8222-222222222222', ownerType: 'session', ownerId: 'closeout-fixture',
        items: [{ productId: '33333333-3333-4333-8333-333333333333', variantId: '44444444-4444-4444-8444-444444444444', productName: 'Synthetic item', sku: 'TEST', quantity: 1, unitPrice: 100, lineTotal: 100 }],
        itemCount: 1, subtotal: 100, discountTotal: 0, taxTotal: 0, grandTotal: 100, currency: 'EGP',
        lastUpdatedUtc: '2026-10-15T00:00:00Z', expiresAtUtc: '2099-01-01T00:00:00Z', appliedCouponCode: null, appliedCoupon: null,
    };
    const fixture = route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><body><div id="fixture"></div></body></html>' });
    try {
        const tab = await storefront.newPage();
        tab.on('pageerror', error => errors.push(error.message));
        await tab.route('**/__quality_closeout__', fixture);
        await tab.route('**/api/**', route => {
            const path = route.request().url().replace(/^https?:\/\/[^/]+/, '').split('?')[0];
            if (!path.startsWith('/api/')) return route.continue();
            if (path === '/api/storefront/cart') return route.fulfill({ status: cartMode === 'valid' ? 200 : cartMode === 'missing' ? 404 : cartMode === 'rejected' ? 403 : 503,
                contentType: 'application/json', body: JSON.stringify(cartMode === 'valid' ? cart : { code: cartMode === 'missing' ? 'CART_NOT_FOUND' : 'CONTROLLED_FAILURE', message: 'Controlled failure' }) });
            return route.fulfill({ contentType: 'application/json', body: '{}' });
        });
        await tab.goto('http://localhost:5174/__quality_closeout__');
        await tab.evaluate(async ({ cart, dependencySource }) => {
            const dep = eval('(' + dependencySource + ')');
            const refresh = await import('/@react-refresh'); refresh.injectIntoGlobalHook(window);
            window.$RefreshReg$ = () => {}; window.$RefreshSig$ = () => type => type;
            const React = (await import('/node_modules/.vite/deps/react.js')).default;
            const { createRoot } = (await import('/node_modules/.vite/deps/react-dom_client.js')).default;
            const source = await (await fetch('/src/components/cart/CartSummary.tsx')).text();
            const { CartSummary } = await import('/src/components/cart/CartSummary.tsx');
            const { MemoryRouter } = await import(source.match(/from "([^"]*react-router-dom[^"]*)"/)[1]);
            const { useCartStore } = await import(dep(source, '/src/store/cartStore.ts'));
            const { useAuthStore } = await import(dep(source, '/src/auth/store/authStore.ts'));
            const { useStoreConfigStore } = await import(dep(source, '/src/store/index.ts'));
            const { setResolvedTenant } = await import('/src/bootstrap/tenantResolver.ts');
            const { setSessionId } = await import('/src/bootstrap/sessionManager.ts');
            setResolvedTenant(cart.tenantId); setSessionId('closeout-isolated-session');
            useAuthStore.setState({ status: 'unauthenticated', customer: null });
            useStoreConfigStore.setState(s => ({ config: { ...s.config, currency: 'EGP', behavior: { ...s.config.behavior, enableGuestCheckout: true } }, isLoaded: true }));
            const root = createRoot(document.getElementById('fixture'));
            root.render(React.createElement(MemoryRouter, null, React.createElement(CartSummary, { subtotal: 100, grandTotal: 100, currency: 'EGP', itemCount: 1, isEmpty: false, onClearCart: () => {} })));
            window.closeout = {
                sync: () => useCartStore.getState().fetchCart(),
                dismiss: () => useCartStore.getState().clearError(),
                state: () => ({ items: useCartStore.getState().cart?.items.length ?? 0, requiresRefresh: useCartStore.getState().requiresRefresh, error: useCartStore.getState().error }),
            };
        }, { cart, dependencySource: dependency.toString() });
        // The localized label is read from the actual component rather than fixed text.
        const checkoutButton = tab.locator('#fixture button').first();
        await tab.evaluate(() => window.closeout.sync());
        await tab.waitForFunction(() => !document.querySelector('#fixture button').disabled);
        check('valid server cart enables checkout', await checkoutButton.isEnabled());
        cartMode = 'unavailable';
        await tab.evaluate(() => window.closeout.sync());
        await tab.waitForFunction(() => document.querySelector('#fixture button').disabled);
        let state = await tab.evaluate(() => window.closeout.state());
        check('transient failure retains items only as unconfirmed reference', state.items === 1 && state.requiresRefresh && /unconfirmed/.test(state.error));
        await tab.evaluate(() => window.closeout.dismiss());
        check('dismissing the error does not enable checkout', await checkoutButton.isDisabled());
        check('refresh recovery remains available after dismissal', await tab.getByRole('button', { name: 'Retry', exact: true }).isVisible());
        cartMode = 'valid';
        await tab.getByRole('button', { name: 'Retry', exact: true }).click();
        await tab.waitForFunction(() => !document.querySelector('#fixture button').disabled);
        check('successful retry restores server-confirmed checkout', await checkoutButton.isEnabled());
        for (const mode of ['missing', 'rejected']) {
            cartMode = mode;
            await tab.evaluate(() => window.closeout.sync());
            state = await tab.evaluate(() => window.closeout.state());
            check(mode + ' cart is not represented as valid data', state.items === 0 && state.requiresRefresh && await checkoutButton.isDisabled());
        }

        const adminTab = await admin.newPage();
        adminTab.on('pageerror', error => errors.push(error.message));
        await adminTab.route('**/__quality_closeout__', fixture);
        await adminTab.route('**/api/**', route => {
            const path = route.request().url().replace(/^https?:\/\/[^/]+/, '').split('?')[0];
            if (!path.startsWith('/api/')) return route.continue();
            if (path.includes('/checkout-attachments/')) {
                attachmentRequests++;
                return route.fulfill({ contentType: 'image/png', path: '.ai/evidence/quality-closeout-20261015/synthetic-preview.png' });
            }
            return route.fulfill({ contentType: 'application/json', body: '{}' });
        });
        await adminTab.goto('http://localhost:5173/__quality_closeout__');
        await adminTab.evaluate(async dependencySource => {
            const dep = eval('(' + dependencySource + ')');
            const refresh = await import('/@react-refresh'); refresh.injectIntoGlobalHook(window);
            window.$RefreshReg$ = () => {}; window.$RefreshSig$ = () => type => type;
            const React = (await import('/node_modules/.vite/deps/react.js')).default;
            const { createRoot } = (await import('/node_modules/.vite/deps/react-dom_client.js')).default;
            const source = await (await fetch('/src/pages/OrderCheckoutAttachmentPreview.jsx')).text();
            const { default: Preview } = await import('/src/pages/OrderCheckoutAttachmentPreview.jsx');
            const { useAuthStore } = await import(dep(source, '/src/store/authStore.js'));
            const root = createRoot(document.getElementById('fixture'));
            let key = 0;
            window.attachment = role => {
                useAuthStore.setState({ user: { role, tenantId: '22222222-2222-4222-8222-222222222222', effectivePermissions: ['storefront.orders.view', ...(role === 'Owner' || role === 'Admin' ? ['storefront.orders.attachments.download'] : [])] } });
                root.render(React.createElement(Preview, { key: ++key, orderId: '11111111-1111-4111-8111-111111111111', fieldLabel: 'Proof', attachment: { attachmentId: '33333333-3333-4333-8333-333333333333', fileName: 'synthetic.png', contentType: 'image/png', mediaType: 'image', sizeBytes: 1 } }));
            };
        }, dependency.toString());
        for (const role of ['Manager', 'Cashier', 'Warehouse']) {
            const before = attachmentRequests;
            await adminTab.evaluate(role => window.attachment(role), role);
            await adminTab.waitForFunction(() => document.querySelector('#fixture').childElementCount === 0);
            check(role + ' has no attachment controls or preview request', await adminTab.getByRole('button').count() === 0 && attachmentRequests === before);
        }
        for (const role of ['Owner', 'Admin']) {
            await adminTab.evaluate(role => window.attachment(role), role);
            await adminTab.getByRole('img').waitFor();
            const download = adminTab.getByRole('button', { name: 'View / Download' });
            check(role + ' receives authenticated preview and download control', await download.isEnabled());
            const before = attachmentRequests;
            await download.click();
            await adminTab.waitForFunction(() => [...document.querySelectorAll('button')].some(b => b.textContent === 'View / Download' && !b.disabled));
            check(role + ' downloads through protected API', attachmentRequests === before + 1);
        }
        return { layer: 'real-components-controlled-HTTP', results, errors, passed: results.filter(r => r.passed).length, failed: results.filter(r => !r.passed).length };
    } catch (error) {
        return { layer: 'real-components-controlled-HTTP', results, errors, failed: 1, exception: error.stack ?? String(error) };
    } finally { await storefront.close(); await admin.close(); }
}

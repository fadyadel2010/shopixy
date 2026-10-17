async page => {
    // Isolated real ShippingStep component fixture. Not an authenticated checkout E2E.
    // No app route guards, auth state, tenant resolver or customer records are changed.
    const sf = page.context().pages().find(p => p.url().includes('/__shipping_component_closure__')) || await page.context().newPage()
    const traffic = [], cases = [], layouts = []
    await sf.unroute('**/api/**')
    await sf.unroute('**/__shipping_component_closure__')
    await sf.route('**/api/**', route => {
        const req = route.request(), path = req.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0]
        if (!path.startsWith('/api/')) return route.continue()
        traffic.push({ method: req.method(), path, intercepted: true })
        return route.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({ code: 'ISOLATED_COMPONENT_NETWORK_BLOCKED' }) })
    })
    await sf.route('**/__shipping_component_closure__', route => route.fulfill({ contentType: 'text/html; charset=utf-8', body: '<!doctype html><html><head><meta charset="utf-8"><title>Isolated ShippingStep fixture</title><link rel="stylesheet" href="/src/styles/index.css"></head><body><p>ISOLATED REAL-COMPONENT TEST — NOT A LIVE CHECKOUT</p><main id="fixture" style="max-width:800px;margin:auto;padding:16px"></main></body></html>' }))
    await sf.goto('http://localhost:5174/__shipping_component_closure__')
    await sf.evaluate(async () => {
        const refresh = await import('/@react-refresh')
        refresh.injectIntoGlobalHook(window)
        window.$RefreshReg$ = () => {}
        window.$RefreshSig$ = () => type => type
        const React = (await import('/node_modules/.vite/deps/react.js')).default
        const { createRoot } = (await import('/node_modules/.vite/deps/react-dom_client.js')).default
        const { ShippingStep } = await import('/src/components/checkout/ShippingStep.tsx')
        const { useCheckoutStore } = await import('/src/store/checkoutStore.ts')
        const { useCartStore } = await import('/src/store/cartStore.ts')
        const { useStoreConfigStore } = await import('/src/store/storeConfigStore.ts')
        const { useLanguageStore, t } = await import('/src/i18n/index.ts')
        const methodId = '20000000-0000-4000-8000-000000000001'
        const cartId = '10000000-0000-4000-8000-000000000001'
        useStoreConfigStore.setState({ config: { ...useStoreConfigStore.getState().config, currency: 'EGP', defaultCountryCode: 'EG' }, isLoaded: true })
        useCartStore.setState({ cart: { cartId, currency: 'EGP', items: [], itemCount: 0 } })
        useCheckoutStore.setState({ checkoutFields: [], checkoutFieldsStatus: 'EMPTY', checkoutFieldsTotalCount: 1, checkoutFieldsLoading: false,
            shippingMethods: [{ id: methodId, name: 'Synthetic carrier service', nameAr: 'خدمة ناقل تجريبية', price: 25, currency: 'EGP', estimatedDays: 3 }],
            shippingMethodId: methodId, shippingMethodsStatus: 'AVAILABLE', shippingMethodsLoading: false, shippingSelectionLoading: false,
            shippingSelectionStatus: 'valid', checkoutSessionId: '50000000-0000-4000-8000-000000000001', checkoutSessionCartId: cartId,
            warehouseResolutionId: '60000000-0000-4000-8000-000000000001', warehouseResolutionVersion: 1, warehouseResolutionStatus: 'RESOLVED', warehouseResolutionExpiresAtUtc: '2099-01-01T00:00:00Z' })
        createRoot(document.getElementById('fixture')).render(React.createElement(ShippingStep, { onContinue: () => {} }))
        // Presentation fixtures only. The real store's API admission/actions are verified separately by Vitest.
        window.shippingClosureFixture = {
            setLanguage: value => useLanguageStore.getState().setLanguage(value),
            set: patch => useCheckoutStore.setState(patch),
            label: key => t(key),
            methodId,
        }
    })
    for (const language of ['en', 'ar']) {
        await sf.evaluate(value => window.shippingClosureFixture.setLanguage(value), language)
        const labels = await sf.evaluate(() => Object.fromEntries(['continueToPayment', 'unavailable', 'notApplicable', 'noEnabledMethods', 'contextRequired', 'methodTitle', 'confirmingAvailability'].map(key => [key, window.shippingClosureFixture.label(`checkout.shipping.${key}`)])))
        for (const width of [1440, 390]) {
            await sf.setViewportSize({ width, height: width === 390 ? 844 : 1000 })
            for (const status of ['AVAILABLE', 'EMPTY', 'NOT_APPLICABLE', 'VALIDATION_REQUIRED', 'UNAVAILABLE', 'ERROR']) {
                await sf.evaluate(status => window.shippingClosureFixture.set({ shippingMethodsStatus: status, shippingMethodsError: null,
                    shippingMethodsLoading: false, shippingSelectionLoading: false, shippingSelectionStatus: 'valid', staleShippingSelection: null }), status)
                const button = sf.getByRole('button', { name: labels.continueToPayment, exact: true })
                await button.waitFor()
                if (await button.isEnabled() !== (status === 'AVAILABLE')) throw new Error(`Continuation gate mismatch: ${language}/${width}/${status}`)
                if (status === 'AVAILABLE') await sf.getByText(language === 'ar' ? 'خدمة ناقل تجريبية' : 'Synthetic carrier service', { exact: true }).waitFor()
                if (['UNAVAILABLE', 'ERROR'].includes(status)) await sf.getByRole('alert').filter({ hasText: labels.unavailable }).waitFor()
                const result = await sf.evaluate(() => ({ width: innerWidth, scrollWidth: document.documentElement.scrollWidth, direction: document.documentElement.dir }))
                if (result.scrollWidth > width + 1 || result.direction !== (language === 'ar' ? 'rtl' : 'ltr')) throw new Error('ShippingStep overflow or direction failure')
                cases.push(`${language}/${width}/${status}`); layouts.push({ language, status, ...result })
            }
            await sf.evaluate(() => window.shippingClosureFixture.set({ shippingMethodsStatus: 'AVAILABLE', shippingSelectionLoading: true }))
            await sf.getByText(labels.confirmingAvailability, { exact: true }).waitFor()
            if (await sf.getByRole('button', { name: labels.continueToPayment, exact: true }).isEnabled()) throw new Error('Pending selection enabled continuation')
            await sf.evaluate(() => window.shippingClosureFixture.set({ shippingSelectionLoading: false, shippingSelectionStatus: 'stale',
                staleShippingSelection: { label: 'Previous synthetic service', message: 'checkout.staleSelection', value: window.shippingClosureFixture.methodId } }))
            if (await sf.getByRole('button', { name: labels.continueToPayment, exact: true }).isEnabled()) throw new Error('Stale selection enabled continuation')
            await sf.screenshot({ path: `C:/Users/fadya/AppData/Local/Temp/opencode/shipping-final/storefront-${language}-${width}.png`, fullPage: true })
            cases.push(`${language}/${width}/pending-and-stale-selection`)
        }
    }
    return { result: 'PASS', mode: 'isolated actual ShippingStep with controlled presentation state; not authenticated checkout E2E', cases, layouts, traffic, realBusinessMutations: 0, realCarrierCalls: 0 }
}

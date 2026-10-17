async page => {
    const results = []
    const check = (name, ok, layer = 'hosted') => { results.push({ name, passed: !!ok, layer }); if (!ok) throw new Error(name) }
    const dictionaries = await page.evaluate(async () => ({ en: await (await fetch('/src/i18n/locales/en.json')).json(), ar: await (await fetch('/src/i18n/locales/ar.json')).json() }))
    const t = dictionaries.en
    await page.goto('http://localhost:5173/products')
    await page.getByPlaceholder(t.products.searchPlaceholder).waitFor()
    const needle = 'QE-NO-MATCH-9d6c4a8e'
    const searchResponse = page.waitForResponse(r => r.url().includes('/api/products') && r.url().includes(needle))
    await page.getByPlaceholder(t.products.searchPlaceholder).fill(needle)
    await page.getByPlaceholder(t.products.searchPlaceholder).press('Enter')
    const products = await searchResponse, productsBody = await products.json()
    await page.getByText(t.products.noMatchingProducts, { exact: true }).waitFor()
    check('product search backend and empty state agree', products.status() === 200 && (Array.isArray(productsBody) ? productsBody : productsBody.items).length === 0)
    await page.getByPlaceholder(t.products.searchPlaceholder).fill('')
    await page.getByPlaceholder(t.products.searchPlaceholder).press('Enter')
    await page.getByRole('button', { name: 'Name', exact: true }).count().then(async n => { if (n) await page.getByRole('button', { name: 'Name', exact: true }).click() })

    for (const [path, strings, addKey] of [['customers', t.customers, 'addCustomer'], ['suppliers', t.suppliers, 'addSupplier']]) {
        await page.goto('http://localhost:5173/' + path)
        await page.getByPlaceholder(strings.searchPlaceholder).waitFor()
        const responsePromise = page.waitForResponse(r => r.url().includes('/api/' + path) && r.url().includes(needle))
        await page.getByPlaceholder(strings.searchPlaceholder).fill(needle)
        const response = await responsePromise, body = await response.json()
        const emptyText = strings.noCustomers || strings.noSuppliers
        await page.getByText(emptyText, { exact: true }).waitFor()
        check(path + ' search backend and empty state agree', response.status() === 200 && body.items.length === 0)
        await page.getByRole('button', { name: strings[addKey], exact: true }).click()
        const form = page.locator('main form').last()
        await form.getByRole('button', { name: t.common.save, exact: true }).click()
        check(path + ' required-field validation blocks blank creation', await form.locator('input[required]').evaluate(input => !input.validity.valid))
        let count = 0, release, startedResolve
        const started = new Promise(resolve => { startedResolve = resolve })
        const held = new Promise(resolve => { release = resolve })
        const fail = async route => { count++; startedResolve(); await held; await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'QE controlled party save failure' }) }) }
        await page.route('**/api/' + path, fail)
        try {
            await form.locator('input[required]').fill('SYNTHETIC intercepted only')
            await form.evaluate(form => { form.requestSubmit(); form.requestSubmit() })
            await started
            check(path + ' rapid submits send one request and disable cancel', count === 1 && await form.getByRole('button', { name: t.common.save, exact: true }).isDisabled() && await form.getByRole('button', { name: t.common.cancel, exact: true }).isDisabled(), 'intercepted-negative-ui')
            const refused = page.waitForResponse(r => r.url().endsWith('/api/' + path) && r.request().method() === 'POST')
            release()
            await refused
            await form.getByRole('button', { name: t.common.save, exact: true }).waitFor()
            await page.waitForFunction(() => !document.querySelector('main form:last-of-type button[type=submit]')?.disabled)
            check(path + ' failed creation retains input for retry', await form.locator('input[required]').inputValue() === 'SYNTHETIC intercepted only', 'intercepted-negative-ui')
            await form.getByRole('button', { name: t.common.cancel, exact: true }).click()
        } finally { release(); await page.unroute('**/api/' + path, fail) }
    }

    // Retention-only settings save: no company/currency/tax value is changed.
    await page.goto('http://localhost:5173/settings')
    await page.getByRole('button', { name: t.settings.saveSettings, exact: true }).waitFor()
    const beforeSettings = await (await page.request.get('http://localhost:5173/api/settings')).json()
    let reads = 0
    const countReads = r => { if (r.url().endsWith('/api/settings') && r.request().method() === 'GET') reads++ }
    page.on('response', countReads)
    try {
        await page.getByRole('button', { name: t.settings.saveSettings, exact: true }).click()
        await page.getByText(t.settings.settingsSaved, { exact: true }).waitFor()
        check('settings success follows both editor and shared-context readback', reads >= 2)
        const after = await (await page.request.get('http://localhost:5173/api/settings')).json()
        check('retention-only settings save preserves authoritative values', JSON.stringify(after) === JSON.stringify(beforeSettings))
    } finally { page.removeListener('response', countReads) }

    for (const lang of ['en', 'ar']) {
        await page.goto('http://localhost:5173/admin/shipping/health')
        await page.getByRole('button', { name: /^(EN|AR)$/ }).waitFor()
        const toggle = page.getByRole('button', { name: lang === 'ar' ? 'AR' : 'EN', exact: true })
        if (await toggle.count()) await toggle.click()
        await page.getByText(dictionaries[lang].shipping.healthScopeWarning, { exact: true }).waitFor()
        check('shipping liveness boundary shown in ' + lang, await page.getByText('All Systems Operational', { exact: true }).count() === 0)
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 1000 })
            await page.waitForFunction(() => document.documentElement.scrollWidth <= window.innerWidth + 1)
            check('shipping health no overflow ' + lang + ' ' + width, true)
            await page.screenshot({ path: '.ai/evidence/pos-admin-quality-20261015/shipping-health-' + lang + '-' + width + '.png', fullPage: true })
        }
    }
    const english = page.getByRole('button', { name: 'EN', exact: true })
    if (await english.count()) await english.click()
    await page.setViewportSize({ width: 1440, height: 1000 })
    await page.goto('http://localhost:5173/storefront/appearance/colors-typography')
    await page.getByLabel('Primary Color', { exact: true }).waitFor()
    const config = await (await page.request.get('http://localhost:5173/api/admin/store-config')).json()
    // Chromium resolves *.localhost; Node's APIRequestContext DNS on this Windows
    // machine does not. Use the actual Storefront origin and browser fetch.
    await page.goto('http://ahramstore.localhost:5174/')
    const storefront = await page.evaluate(async () => {
        const response = await fetch('/api/storefront/store-config')
        return { status: response.status, body: await response.json() }
    })
    check('real Storefront materialization matches published primary color', storefront.status === 200 && storefront.body.theme.colors.primary === config.liveTheme.colors.primary)
    await page.goto('http://localhost:5173/storefront/appearance/colors-typography')
    return { results, realPartyCreations: 0, settingsValuesChanged: false }
}

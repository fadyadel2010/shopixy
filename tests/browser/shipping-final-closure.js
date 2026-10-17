async page => {
    // Normal POS authentication must already have succeeded. No fabricated auth or permissions.
    // All business API traffic, including unmatched reads, is fulfilled locally.
    // Only normal read-only cookie-session validation is allowed through.
    const id = '11111111-1111-4111-8111-111111111111'
    const orderId = '22222222-2222-4222-8222-222222222222'
    const warehouseId = '33333333-3333-4333-8333-333333333333'
    const accountId = '44444444-4444-4444-8444-444444444444'
    const countryId = '55555555-5555-4555-8555-555555555555'
    const stateId = '66666666-6666-4666-8666-666666666666'
    const localityId = '77777777-7777-4777-8777-777777777777'
    // Windows Vite watcher may retain old transforms. Fetch a distinct transform key,
    // without restarting the dev server or modifying application/auth state.
    await page.unroute('**/src/**')
    await page.route('**/src/**', async route => {
        const url = route.request().url()
        if (!/\/src\/(modules\/shipping|pages\/Warehouses|i18n\/locales)/.test(url)) return route.continue()
        const response = await route.fetch({ url: url + (url.includes('?') ? '&' : '?') + 'shippingClosure=final3' })
        await route.fulfill({ response })
    })
    const dictionaries = await page.evaluate(async () => ({ en: await (await fetch('/src/i18n/locales/en.json')).json(), ar: await (await fetch('/src/i18n/locales/ar.json')).json() }))
    const codes = ['manual', 'bosta', 'aramex', 'dhl', 'fedex', 'mylerz']
    const capabilities = ['ProductionReady', 'Partial', 'Partial', 'Partial', 'NotCertified', 'Unavailable']
    const carriers = codes.map((carrierCode, index) => ({ carrierCode, displayName: carrierCode === 'manual' ? 'Manual' : carrierCode === 'dhl' ? 'DHL Express' : carrierCode === 'fedex' ? 'FedEx' : carrierCode[0].toUpperCase() + carrierCode.slice(1),
        accountRequired: carrierCode !== 'manual', isExternal: carrierCode !== 'manual', readinessRequired: true, productionCapability: capabilities[index],
        environmentSupported: index < 4, connectionTestSupported: ['bosta', 'aramex', 'dhl'].includes(carrierCode), connectionTestMode: carrierCode === 'dhl' ? 'LocalOnly' : index < 3 && index > 0 ? 'Remote' : 'Unsupported',
        credentialFields: [{ key: 'apiKey', label: 'API Key', required: true, isSecret: true, target: 'ApiKey' }],
        serviceLevels: index > 3 ? [] : [{ code: carrierCode === 'dhl' ? 'express' : 'standard', name: 'Synthetic supported service', typicalDaysMin: 1, typicalDaysMax: 3 }] }))
    const accounts = carriers.filter(c => c.accountRequired).map((c, index) => ({ id: index === 0 ? accountId : `00000000-0000-4000-8000-00000000000${index}`, carrierCode: c.carrierCode,
        environment: 'Sandbox', state: 'Draft', operationalState: 'Blocked', healthState: 'Warning', credentialReferences: [], warehouseAssignments: [], policies: [], serviceLevels: [],
        lastConnectionTest: null, integrationReadiness: { enabled: false, canExecute: false, environment: 'Sandbox', productionCapability: c.productionCapability, credentialAvailability: 'Available', blockers: ['CARRIER_CONFIGURATION_DISABLED'] } }))
    const warehouse = { id: warehouseId, name: 'INTERCEPTED warehouse', code: 'FIXTURE', address: 'Saved synthetic street', phone: '02000000000', isActive: true, isDefault: true }
    const traffic = [], cases = [], layouts = []
    let carrier = 'bosta', detailsFailure = false, admissionCount = 0
    const fulfill = (route, data, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(data), headers: { 'X-Shopixy-Test-Traffic': 'intercepted-shipping-final' } })
    await page.unroute('**/api/**')
    await page.route('**/api/**', async route => {
        const req = route.request(), url = req.url(), path = url.replace(/^https?:\/\/[^/]+/, '').split('?')[0]
        if (!path.startsWith('/api/')) return route.continue() // Vite source modules, not operational HTTP
        const method = req.method()
        // Normal cookie-session validation is read-only authentication, not a fixture.
        if (method === 'GET' && path === '/api/auth/session') return route.continue()
        traffic.push({ method, path, query: url.split('?')[1] || '', intercepted: true, ...(!['GET', 'HEAD', 'OPTIONS'].includes(method) ? { body: req.postDataJSON() } : {}) })
        if (path === '/api/admin/shipping/supported-carriers') return fulfill(route, carriers)
        if (path === '/api/admin/shipping/carrier-accounts') return fulfill(route, accounts)
        if (path === '/api/admin/shipping/audit') return fulfill(route, [])
        if (path.endsWith('/connection-tests')) return fulfill(route, { id: 'test', status: 'Failed', failureCode: 'SYNTHETIC_PROVIDER_UNAVAILABLE', failureReason: 'INTERCEPTED failure — HTTP 200 is not verification' })
        if (path === '/api/shippingmethods') return fulfill(route, method === 'GET' ? [] : { id, ...req.postDataJSON() })
        if (path === '/api/warehouses') return fulfill(route, [warehouse])
        if (path.endsWith('/address-admission/places')) {
            const params = url.split('?')[1] || ''
            return fulfill(route, { catalogReleaseId: id, places: !params.includes('countryPlaceId') ? [{ placeId: countryId, placeKind: 'Country', canonicalCode: 'US', displayName: 'United States', postalCodePolicy: 'Required' }] :
                !params.includes('parentPlaceId') ? [{ placeId: stateId, placeKind: 'AdministrativeArea', canonicalCode: 'NY', displayName: 'New York' }] :
                params.includes(`parentPlaceId=${stateId}`) ? [{ placeId: localityId, placeKind: 'Locality', canonicalCode: 'NYC', displayName: 'New York City' }] : [] })
        }
        if (path.endsWith('/address-admission')) {
            if (++admissionCount === 1) return fulfill(route, { code: 'PostalCodeInvalid', message: 'INTERCEPTED invalid postal input' }, 400)
            return fulfill(route, { warehouseId, addressAdmissionEvidenceId: accountId, catalogReleaseId: id, addressFingerprintSha256: 'a'.repeat(64) })
        }
        if (path === `/api/admin/shipments/${id}`) return detailsFailure ? fulfill(route, { message: 'INTERCEPTED shipment read unavailable' }, 503) : fulfill(route, {
            shipmentId: id, orderId, shipmentNumber: 'INTERCEPTED-SHIPPING-CLOSURE', status: 'PendingExternalRegistration', carrierName: carrier, carrierCode: carrier, serviceLevel: carrier === 'dhl' ? 'express' : 'standard',
            externalRegistrationState: 'ReconciliationRequired', externalRegistrationError: 'INTERCEPTED unresolved registration', externalRegistrationAttemptCount: 1,
            awbNumber: 'AWB-RECORDED', labelData: 'JVBERi0x', labelFormat: 'pdf', trackingNumber: 'TRACK-RECORDED',
            currency: 'EGP', shippingCost: 25, isCashOnDelivery: true, codAmount: 150, codCollected: false, packageCount: 1,
            createdAtUtc: '2026-10-11T12:00:00Z', recipient: { name: 'Nested synthetic receiver', address: 'Synthetic street', city: 'Cairo', country: 'EG' }, shippingMethod: { name: 'Synthetic method' }
        })
        if (path === '/api/admin/shipments') return fulfill(route, { page: 1, pageSize: 20, totalCount: 1, totalPages: 1, items: [{ id, shipmentId: id, shipmentNumber: 'INTERCEPTED-SHIPPING-CLOSURE', status: 'PendingExternalRegistration', carrierCode: 'fedex', isCashOnDelivery: true, codAmount: 150, currency: 'EGP', createdAt: '2026-10-11T12:00:00Z' }] })
        if (path.endsWith('/parcel-evidence')) return fulfill(route, { canMeasure: true, evidence: null, isInternational: false, contents: [] })
        if (path.includes(orderId) && path.includes('fulfillment')) return fulfill(route, { orderId, availableActions: [] })
        if (path.includes('/settings')) return fulfill(route, { currencyCode: 'EGP', currencySymbol: 'EGP' })
        if (!['GET', 'HEAD', 'OPTIONS'].includes(method)) return fulfill(route, { code: 'UNMATCHED_MUTATION_BLOCKED' }, 409)
        return fulfill(route, []) // never pass unmatched business reads through
    })
    const language = async value => {
        await page.getByRole('button', { name: /^(EN|AR)$/, exact: true }).waitFor()
        const toggle = page.getByRole('button', { name: value === 'en' ? 'EN' : 'AR', exact: true })
        if (await toggle.count()) await toggle.click()
        await page.getByRole('button', { name: value === 'en' ? 'AR' : 'EN', exact: true }).waitFor()
    }
    const layout = async name => {
        const result = await page.evaluate(() => ({ width: innerWidth, scrollWidth: document.documentElement.scrollWidth, direction: document.documentElement.dir }))
        layouts.push({ name, ...result })
        if (result.scrollWidth > result.width + 1) throw new Error(`Page-level overflow in ${name}: ${JSON.stringify(result)}`)
    }
    for (const lang of ['en', 'ar']) {
        const t = dictionaries[lang]
        for (const size of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
            await page.setViewportSize(size)
            await page.goto(`http://localhost:5173/admin/shipping/shipments/${id}`)
            await language(lang)
            for (const code of codes) {
                carrier = code
                await page.getByRole('button', { name: t.common.refresh, exact: true }).last().click()
                await page.getByText('Nested synthetic receiver', { exact: true }).waitFor()
                await page.getByRole('link', { name: t.shipping.closure.storedLabel, exact: true }).waitFor()
                await page.getByText(t.shipping.closure.moneyBoundary, { exact: true }).waitFor()
                if (['dhl', 'fedex'].includes(code)) await page.getByRole('heading', { name: t.shipping.parcel.title }).waitFor()
                cases.push(`${lang}/${size.width}/${code}/stored-evidence-unresolved-COD`)
                await layout(`${lang}/${size.width}/details/${code}`)
            }
            await page.screenshot({ path: `C:/Users/fadya/AppData/Local/Temp/opencode/shipping-final/details-${lang}-${size.width}.png`, fullPage: true })
            await page.goto('http://localhost:5173/admin/shipping/setup')
            await page.getByRole('tab', { name: t.shippingAdministration.tabs.carrierAccounts, exact: true }).click()
            await page.getByRole('heading', { name: 'Mylerz', exact: true }).waitFor()
            for (const name of ['Mylerz', 'FedEx']) {
                const card = page.getByRole('article').filter({ has: page.getByRole('heading', { name, exact: true }) })
                if (await card.getByRole('button', { name: t.shippingAdministration.account.actions.activate, exact: true }).isEnabled()) throw new Error(`${name} activation not gated`)
            }
            await layout(`${lang}/${size.width}/accounts`)
            for (const tab of ['health', 'audit']) {
                await page.getByRole('tab', { name: t.shippingAdministration.tabs[tab], exact: true }).click()
                await layout(`${lang}/${size.width}/${tab}`)
            }
            await page.getByRole('tab', { name: t.shippingAdministration.tabs.shippingMethods, exact: true }).click()
            await page.getByRole('button', { name: '+ ' + t.shipping.addMethod, exact: true }).click()
            const dialog = page.getByRole('dialog')
            await dialog.getByLabel(t.shipping.closure.carrier, { exact: true }).selectOption('mylerz')
            await dialog.getByText(t.shipping.closure.serviceUnavailable, { exact: true }).waitFor()
            if (await dialog.getByRole('button', { name: t.common.save, exact: true }).isEnabled()) throw new Error('Uncertified service accepted')
            await layout(`${lang}/${size.width}/methods-dialog`)
            await dialog.getByRole('button', { name: t.common.cancel, exact: true }).click()
            await page.goto('http://localhost:5173/warehouses')
            await page.getByRole('button', { name: t.shipping.warehouseAdmission.title, exact: true }).click()
            await page.getByRole('dialog').waitFor()
            await page.getByRole('option', { name: 'US — United States', exact: true }).waitFor({ state: 'attached' })
            await layout(`${lang}/${size.width}/warehouse-dialog`)
            await page.getByRole('dialog').getByRole('button', { name: t.shipping.warehouseAdmission.close, exact: true }).click()
            await page.goto('http://localhost:5173/admin/shipping/shipments')
            await page.getByRole('button', { name: 'INTERCEPTED-SHIPPING-CLOSURE', exact: true }).waitFor()
            await layout(`${lang}/${size.width}/list`)
            cases.push(`${lang}/${size.width}/accounts-health-audit-methods-warehouse-list-gates`)
        }
    }
    await page.setViewportSize({ width: 1440, height: 1000 })
    await language('en')
    const t = dictionaries.en
    await page.goto('http://localhost:5173/warehouses')
    await page.getByRole('button', { name: t.shipping.warehouseAdmission.title, exact: true }).click()
    const w = t.shipping.warehouseAdmission
    await page.getByLabel(w.countryPlaceId, { exact: true }).selectOption(countryId)
    await page.getByLabel(w.administrativeAreaPlaceId, { exact: true }).selectOption(stateId)
    await page.getByLabel(w.localityPlaceId, { exact: true }).selectOption(localityId)
    await page.getByLabel(w.street, { exact: true }).fill(warehouse.address)
    await page.getByLabel(w.postal, { exact: true }).fill('10001')
    await page.getByLabel(w.reason, { exact: true }).fill('Synthetic admission verification only')
    await page.getByRole('button', { name: w.save, exact: true }).click()
    await page.getByRole('alert').filter({ hasText: 'PostalCodeInvalid' }).waitFor()
    if (await page.getByLabel(w.postal, { exact: true }).inputValue() !== '10001') throw new Error('Admission failure lost input')
    await page.getByRole('button', { name: w.save, exact: true }).click()
    await page.getByText(accountId, { exact: true }).waitFor()
    cases.push('warehouse-400-preservation-success-contract')
    await page.goto('http://localhost:5173/admin/shipping/shipments')
    await page.getByLabel(t.shipping.closure.carrier, { exact: true }).selectOption('fedex')
    await page.getByRole('button', { name: t.shipping.closure.moreFilters, exact: true }).click()
    await page.getByLabel(t.shipping.closure.payment, { exact: true }).selectOption('false')
    await page.getByLabel(t.shipping.closure.fromDate, { exact: true }).fill('2026-10-01')
    await page.getByLabel(t.shipping.closure.toDate, { exact: true }).fill('2026-10-11')
    await page.waitForResponse(r => r.url().includes('/api/admin/shipments?') && r.url().includes('toDate=2026-10-11'))
    if (!traffic.some(r => r.query.includes('carrier=fedex') && r.query.includes('isCod=false') && r.query.includes('fromDate=2026-10-01') && r.query.includes('toDate=2026-10-11'))) throw new Error('Filter body contract mismatch')
    cases.push('list-filter-query-contract')
    detailsFailure = true
    await page.goto(`http://localhost:5173/admin/shipping/shipments/${id}`)
    await page.getByText('INTERCEPTED shipment read unavailable', { exact: true }).waitFor()
    detailsFailure = false
    await page.getByRole('button', { name: t.common.retry, exact: true }).click()
    await page.getByText('Nested synthetic receiver', { exact: true }).waitFor()
    cases.push('details-read-failure-retry')
    await page.goto('http://localhost:5173/admin/shipping/setup')
    await page.getByRole('tab', { name: t.shippingAdministration.tabs.carrierAccounts, exact: true }).click()
    const bosta = page.getByRole('article').filter({ has: page.getByRole('heading', { name: 'Bosta', exact: true }) })
    await bosta.locator('input[type=password]').fill('synthetic-key-not-credentials')
    await bosta.locator('form').last().locator('button[type=submit]').click()
    await bosta.getByText(/SYNTHETIC_PROVIDER_UNAVAILABLE/).waitFor()
    cases.push('connection-HTTP200-failed-not-success')
    await page.screenshot({ path: 'C:/Users/fadya/AppData/Local/Temp/opencode/shipping-final/accounts-connection-failed.png', fullPage: true })
    await page.getByRole('tab', { name: t.shippingAdministration.tabs.shippingMethods, exact: true }).click()
    await page.getByRole('button', { name: '+ ' + t.shipping.addMethod, exact: true }).click()
    const methodDialog = page.getByRole('dialog')
    await methodDialog.getByLabel(t.shipping.nameEn, { exact: true }).fill('INTERCEPTED supported method')
    await methodDialog.getByLabel(t.shippingAdministration.methods.fulfillmentWarehouse, { exact: true }).selectOption(warehouseId)
    await methodDialog.getByRole('button', { name: t.common.save, exact: true }).click()
    await page.getByText(t.shipping.created, { exact: false }).waitFor()
    if (!traffic.some(r => r.method === 'POST' && r.path === '/api/shippingmethods' && r.body.carrierCode === 'manual' && r.body.serviceLevel === 'standard' && r.body.fulfillmentWarehouseId === warehouseId)) throw new Error('Supported shipping method payload lost purchased execution scope')
    cases.push('supported-method-submit-body-contract')
    return { result: 'PASS', authentication: 'normal POS login, unchanged route guards', cases, layouts, traffic, realProviderCalls: 0, realBusinessMutations: 0 }
}

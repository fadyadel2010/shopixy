async page => {
    await page.setViewportSize({ width: 1440, height: 1000 })
    // Run with playwright-cli -s=opencode-ui-test run-code --filename=tests/browser/dhl-parcel-evidence.js.
    // Authenticated real application, synthetic intercepted shipment/evidence/quote traffic only.
    const id = '11111111-1111-4111-8111-111111111111'
    const orderId = '22222222-2222-4222-8222-222222222222'
    const traffic = []
    let evidence = null
    let saves = 0
    let estimates = 0
    if (await page.getByRole('button', { name: 'EN', exact: true }).count())
        await page.getByRole('button', { name: 'EN', exact: true }).click()
    const fulfill = (route, data, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(data), headers: { 'X-Shopixy-Test-Traffic': 'intercepted-synthetic-dhl-parcel' } })
    await page.unroute('**/api/**')
    await page.route('**/api/**', async route => {
        const req = route.request()
        const path = req.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0]
        if (path === `/api/admin/shipments/${id}`) return fulfill(route, {
            shipmentId: id, orderId, shipmentNumber: 'INTERCEPTED-DHL-PARCEL', status: 'PendingExternalRegistration', carrierName: 'dhl', carrierCode: 'dhl',
            currency: 'EGP', shippingCost: 0, createdAtUtc: '2026-10-11T12:00:00Z', recipient: { name: 'Synthetic Receiver', address: 'Synthetic address', city: 'Giza', country: 'EG' }, shippingMethod: { name: 'Synthetic DHL' }
        })
        if (path.includes(orderId) && path.includes('fulfillment')) return fulfill(route, { orderId, availableActions: [] })
        if (path.endsWith(`${id}/parcel-evidence`)) {
            if (req.method() === 'GET') return fulfill(route, { evidence, canMeasure: !evidence, isInternational: true, registrationState: 'ReconciliationRequired', registrationError: 'INTERCEPTED outcome only', contents: [{ orderItemId: 'synthetic-line', productId: 'synthetic-product', quantity: 2 }] })
            const body = req.postDataJSON()
            traffic.push({ method: req.method(), path, body, intercepted: true })
            saves++
            if (saves === 1) return fulfill(route, { code: 'PARCEL_MEASUREMENTS_OUT_OF_RANGE' }, 400)
            evidence = { measurement: body }
            return fulfill(route, evidence)
        }
        if (path.endsWith(`${id}/parcel-quote`)) {
            traffic.push({ method: req.method(), path, intercepted: true })
            estimates++
            if (estimates === 1) return fulfill(route, { success: false, errorCode: 'DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED' }, 409)
            return fulfill(route, { success: true, quotes: [{ serviceName: 'INTERCEPTED DHL estimate', cost: 123.45, currency: 'EGP' }] })
        }
        if (!['GET', 'HEAD', 'OPTIONS'].includes(req.method())) {
            traffic.push({ method: req.method(), path, blocked: true })
            return fulfill(route, { code: 'SYNTHETIC_TEST_MUTATION_BLOCKED' }, 409)
        }
        return route.continue()
    })
    await page.goto(`http://localhost:5173/admin/shipping/shipments/${id}`)
    await page.getByRole('heading', { name: 'Measured packed parcel' }).waitFor()
    const save = page.getByRole('button', { name: 'Save measurements', exact: true })
    if (await save.isEnabled()) throw new Error('Missing evidence enabled submission')
    await page.getByLabel('Packed length', { exact: true }).fill('231')
    await page.getByLabel('Packed width', { exact: true }).fill('145')
    await page.getByLabel('Packed height', { exact: true }).fill('89')
    await page.getByLabel('Dimension unit', { exact: true }).selectOption('mm')
    await page.getByLabel('Packed gross weight', { exact: true }).fill('1750')
    await page.getByLabel('Weight unit', { exact: true }).selectOption('g')
    await page.getByLabel('Parcel reference (letters, digits, hyphen)', { exact: true }).fill('BOX-1')
    await save.click()
    await page.getByRole('alert').filter({ hasText: 'PARCEL_MEASUREMENTS_OUT_OF_RANGE' }).waitFor()
    if (await page.getByLabel('Packed gross weight', { exact: true }).inputValue() !== '1750') throw new Error('Validation lost measurement')
    await page.screenshot({ path: '.artifacts/dhl-parcel/browser-validation-en.png', fullPage: true })
    await save.click()
    await page.getByText('Parcel measurements saved.', { exact: true }).waitFor()
    if (JSON.stringify(traffic[0].body) !== JSON.stringify(traffic[1].body)) throw new Error('Measurement changed during recovery')
    await page.getByRole('button', { name: 'Request carrier estimate', exact: true }).click()
    await page.getByRole('alert').filter({ hasText: 'DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED' }).waitFor()
    await page.getByRole('button', { name: 'Request carrier estimate', exact: true }).click()
    await page.getByText('INTERCEPTED DHL estimate: 123.45 EGP', { exact: true }).waitFor()
    await page.screenshot({ path: '.artifacts/dhl-parcel/browser-estimate-en.png', fullPage: true })
    await page.getByRole('button', { name: 'AR', exact: true }).click()
    await page.getByRole('heading', { name: 'قياسات الطرد بعد التغليف' }).waitFor()
    const section = page.getByRole('region', { name: 'قياسات الطرد بعد التغليف' })
    if (await section.getAttribute('dir') !== 'rtl') throw new Error('Arabic parcel workflow is not RTL')
    await page.screenshot({ path: '.artifacts/dhl-parcel/browser-ar-rtl.png', fullPage: true })
    await page.reload()
    await page.getByText('تم حفظ قياسات الطرد.', { exact: true }).waitFor()
    if (await page.getByLabel('الوزن الإجمالي بعد التغليف', { exact: true }).inputValue() !== '1750') throw new Error('Reload changed immutable measurement')
    if (await page.getByLabel('الطول بعد التغليف', { exact: true }).isEnabled()) throw new Error('Reload allowed immutable mutation')
    return { result: 'PASS', intercepted: true, saves, estimates, traffic, cases: ['missing evidence', 'unit-labelled entry', '400 entry preservation', 'immutable save', 'readiness block', 'estimate presentation', 'Arabic RTL', 'reload preservation'], actualProviderCalls: 0 }
}

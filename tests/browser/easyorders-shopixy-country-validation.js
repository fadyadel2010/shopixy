async page => {
    const before = await page.evaluate(async () => {
        const response = await fetch('/api/shippingmethods?activeOnly=false', { credentials: 'include', cache: 'no-store' })
        if (!response.ok) throw new Error('Method readback failed: ' + response.status)
        return await response.json()
    })
    const attemptedWrites = []
    const guard = route => {
        if (['GET', 'HEAD', 'OPTIONS'].includes(route.request().method())) return route.continue()
        attemptedWrites.push({ method: route.request().method(), path: '/api/shippingmethods' })
        return route.abort('blockedbyclient')
    }
    await page.route('**/api/shippingmethods**', guard)
    try {
        await page.getByRole('button', { name: 'Edit', exact: true }).first().click()
        const dialog = page.getByRole('dialog')
        const countryScope = await dialog.getByRole('combobox', { name: 'Country scope', exact: true }).inputValue()
        const warehouse = await dialog.getByRole('combobox', { name: 'Fulfillment Warehouse', exact: true }).inputValue()
        const scopeMatchesReadback = countryScope === (before[0].availableCountries == null ? 'all' : 'selected')
        const warehouseMatchesReadback = warehouse === (before[0].fulfillmentWarehouseId || '')
        await dialog.getByRole('combobox', { name: 'Country scope', exact: true }).selectOption('selected')
        await dialog.getByRole('button', { name: 'Update', exact: true }).click()
        const validationVisible = await dialog.getByRole('alert').isVisible()
        const validationCode = validationVisible ? 'EMPTY_SELECTED_COUNTRIES_REJECTED' : 'NOT_OBSERVED'
        await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
        const after = await page.evaluate(async () => {
            const response = await fetch('/api/shippingmethods?activeOnly=false', { credentials: 'include', cache: 'no-store' })
            if (!response.ok) throw new Error('Final method readback failed: ' + response.status)
            return await response.json()
        })
        return { observedUtc: new Date().toISOString(), actualAdminUi: true, methodCount: before.length,
            scopeMatchesReadback, warehouseMatchesReadback, validationVisible, validationCode,
            attemptedWrites, allReadbackFieldsUnchanged: JSON.stringify(before) === JSON.stringify(after),
            qualification: 'Readback and client-negative validation only. No real setting save or merchant-data mutation submitted; persistence roundtrip is separately covered by controlled service/UI tests.' }
    } finally { await page.unroute('**/api/shippingmethods**', guard) }
}

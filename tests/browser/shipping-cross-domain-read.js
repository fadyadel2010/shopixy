async page => {
    // Existing authenticated POS session. GET/navigation only; no provider tests,
    // configuration changes, fulfillment commands or financial operations.
    const results = await page.evaluate(async () => {
        const api = await import('/src/modules/shipping-administration/shippingAdministrationApi.js')
        const accounts = await api.fetchCarrierAccounts()
        return accounts.filter(a => ['bosta', 'aramex', 'dhl', 'mylerz'].includes(a.carrierCode)).map(a => ({
            carrier: a.carrierCode, environment: a.environment, state: a.state,
            operationalState: a.operationalState, currentConnectionEvidence: a.connectionTestIsCurrent,
            connectionStatus: a.lastConnectionTest?.status, connectionCode: a.lastConnectionTest?.failureCode,
            connectionTestedUtc: a.lastConnectionTest?.testedAtUtc,
            enabled: a.integrationReadiness?.enabled, canExecute: a.integrationReadiness?.canExecute,
            enabledServices: a.serviceLevels.filter(s => s.isEnabled).length,
            enabledWarehouseAssignments: a.warehouseAssignments.filter(w => w.isEnabled).length,
            operations: a.operations.map(o => ({ operation: o.operation, implementation: o.implementation, accountReady: o.accountReady }))
        }))
    })
    const t = await page.evaluate(async () => (await (await fetch('/src/i18n/locales/en.json')).json()).shippingAdministration)
    await page.getByRole('tab', { name: t.tabs.carrierAccounts, exact: true }).click()
    for (const carrier of ['Bosta', 'Aramex', 'DHL Express', 'Mylerz']) {
        const card = page.getByRole('article').filter({ has: page.getByRole('heading', { name: carrier, exact: true }) })
        await card.getByRole('heading', { name: carrier, exact: true }).waitFor()
        if (await card.locator('input[type=password]').evaluateAll(inputs => inputs.some(i => i.value.length)))
            throw new Error('Saved secret exposed in a carrier input')
    }
    if (results.length !== 4 || results.some(r => r.canExecute || r.enabled || r.enabledServices))
        throw new Error('Configured readiness differs from expected disabled/unqualified boundary; investigate, do not mutate')
    return { observedUtc: new Date().toISOString(), unmocked: true, providerRequests: 0, secretInputsEmpty: true, accounts: results }
}

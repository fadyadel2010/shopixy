async page => {
    // No credentials in this script. Existing saved credentials are retained.
    // Only explicitly supplied Bosta/DHL accounts receive non-mutating tests.
    const dictionaries = await page.evaluate(async () => ({ en: await (await fetch('/src/i18n/locales/en.json')).json(), ar: await (await fetch('/src/i18n/locales/ar.json')).json() }))
    const t = dictionaries.en, labels = t.shippingAdministration.connection
    await page.setViewportSize({ width: 1440, height: 1000 })
    await page.goto('http://localhost:5173/ecommerce/shipping-methods')
    await page.getByRole('button', { name: /^(EN|AR)$/, exact: true }).waitFor()
    const english = page.getByRole('button', { name: 'EN', exact: true })
    if (await english.count()) await english.click()
    await page.getByRole('tab', { name: t.shippingAdministration.tabs.carrierAccounts, exact: true }).click()
    const before = await (await page.request.get('http://localhost:5173/api/admin/shipping/carrier-accounts')).json()
    const evidence = []
    for (const [carrier, displayName] of [['bosta', 'Bosta'], ['dhl', 'DHL Express']]) {
        const card = page.getByRole('article').filter({ has: page.getByRole('heading', { name: displayName, exact: true }) })
        const account = before.find(item => item.carrierCode === carrier)
        if (!account?.savedCredentialFields?.includes('apiKey')) throw new Error(`No saved credential: ${carrier}`)
        if (await card.locator('input[name=ApiKey]').inputValue()) throw new Error('Saved secret exposed')
        // Retention-only Save, no replacements/settings supplied. Then prove the
        // retained credential still reaches its real provider and survives reload.
        await card.getByRole('button', { name: t.common.save, exact: true }).click()
        await card.getByText(labels.saved, { exact: true }).waitFor()
        const response = page.waitForResponse(r => r.url().endsWith(`/carrier-accounts/${account.id}/connection-tests`) && r.request().method() === 'POST')
        await card.getByRole('button', { name: labels.test, exact: true }).click()
        const loading = await card.getByRole('status').filter({ hasText: labels.testing }).count() > 0
        const result = await response, body = await result.json()
        const details = body.data || body.details || body.Details || body
        const succeeded = details.succeeded ?? details.Succeeded
        const code = details.failureCode || details.FailureCode || null
        if (succeeded === true) await card.getByText(labels.authenticated, { exact: true }).first().waitFor()
        else await card.getByRole('alert').filter({ hasText: code || /failed/i }).waitFor()
        evidence.push({ carrier, retainedSaveConfirmed: true, loadingVisible: loading, http: result.status(), succeeded, failureCode: code,
            environment: account.environment, secretInputsEmpty: !await card.locator('input[name=ApiKey]').inputValue() })
    }
    await page.reload()
    await page.getByRole('tab', { name: t.shippingAdministration.tabs.carrierAccounts, exact: true }).click()
    const after = await (await page.request.get('http://localhost:5173/api/admin/shipping/carrier-accounts')).json()
    const audit = await (await page.request.get('http://localhost:5173/api/admin/shipping/audit')).json()
    const cards = []
    for (const account of after.filter(item => ['bosta', 'aramex', 'dhl', 'mylerz'].includes(item.carrierCode))) {
        cards.push({ carrier: account.carrierCode, environment: account.environment || null, savedFields: account.savedCredentialFields,
            currentTest: account.connectionTestIsCurrent, status: account.lastConnectionTest?.status || 'NotTested', failureCode: account.lastConnectionTest?.failureCode || null,
            testedAtUtc: account.lastConnectionTest?.testedAtUtc || null, operationalState: account.operationalState,
            enabled: account.integrationReadiness.enabled, executable: account.integrationReadiness.canExecute,
            versionedAuthenticationAudit: !!account.lastConnectionTest && audit.some(item => item.carrierAccountId === account.id && item.reason === `ConnectionAuthentication:v1:${account.lastConnectionTest.id}`) })
    }
    if (cards.some(item => item.enabled || item.executable)) throw new Error('Connectivity changed operation enablement')
    return { unmocked: true, priorCredentialsReplaced: false, evidence, afterReload: cards }
}

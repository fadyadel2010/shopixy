async page => {
    // Deterministic negative UI fixtures only. No successful provider result is
    // fabricated and no credential mutation reaches the user's backend.
    const codes = ['bosta', 'aramex', 'dhl', 'mylerz']
    const definitions = await (await page.request.get('http://localhost:5173/api/admin/shipping/supported-carriers')).json()
    const dictionaries = await page.evaluate(async () => ({ en: await (await fetch('/src/i18n/locales/en.json')).json(), ar: await (await fetch('/src/i18n/locales/ar.json')).json() }))
    const accounts = codes.map((carrierCode, index) => ({ id: `10000000-0000-4000-8000-00000000000${index}`, carrierCode,
        state: 'Draft', environment: 'Production', healthState: 'Blocked', operationalState: 'NotReady',
        serviceLevels: [], credentialReferences: [], warehouseAssignments: [], policies: [], savedCredentialFields: [],
        connectionTestIsCurrent: false, lastConnectionTest: null, integrationReadiness: { enabled: false, canExecute: false, credentialAvailability: 'Missing', blockers: [] } }))
    const traffic = [], cases = [], layouts = []
    let failure = 'INVALID_CREDENTIALS'
    const fulfill = (route, data, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(data) })
    await page.route('**/api/**', async route => {
        const request = route.request(), path = request.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0]
        if (!path.startsWith('/api/')) return route.continue()
        if (path === '/api/auth/session' && request.method() === 'GET') return route.continue()
        if (path === '/api/admin/shipping/supported-carriers') return fulfill(route, definitions)
        if (path === '/api/admin/shipping/carrier-accounts') return fulfill(route, accounts)
        if (path === '/api/admin/shipping/audit' || path === '/api/warehouses') return fulfill(route, [])
        if (path.endsWith('/connection-tests')) {
            const account = accounts.find(item => path.includes(item.id))
            if (!account) return fulfill(route, { succeeded: false, failureCode: 'FIXTURE_ACCOUNT_NOT_FOUND' }, 404)
            const command = request.postDataJSON()
            traffic.push({ carrier: account.carrierCode, method: request.method(), path, fields: Object.keys(command), extraFields: Object.keys(command.extraConfig || {}), saveOnly: command.saveOnly, useSavedCredentials: command.useSavedCredentials, fixture: true })
            await page.waitForTimeout(250) // observable loading; not a provider delay
            if (command.saveOnly) {
                const definition = definitions.find(item => item.carrierCode === account.carrierCode)
                account.savedCredentialFields = definition.credentialFields.filter(field => field.required).map(field => field.key)
                account.environment = command.useSandbox ? 'Sandbox' : 'Production'
                account.integrationReadiness.credentialAvailability = 'Available'
                account.connectionTestIsCurrent = false; account.lastConnectionTest = null
                return fulfill(route, { succeeded: true, blockers: ['CREDENTIALS_SAVED_NOT_CONNECTION_VERIFIED'] })
            }
            account.connectionTestIsCurrent = true
            account.lastConnectionTest = { id: `fixture-test-${Date.now()}`, status: 'Failed', environment: account.environment, failureCode: failure, failureReason: 'Synthetic refusal. No provider connection was made.', testedAtUtc: new Date().toISOString() }
            if (failure === 'NETWORK_ERROR') return route.abort('failed')
            if (failure.startsWith('CREDENTIAL_')) return fulfill(route, { code: 'INVALID_REQUEST', message: 'Invalid request.', data: { Succeeded: false, FailureCode: failure, Blockers: [failure] } }, 400)
            return fulfill(route, { succeeded: false, failureCode: failure, blockers: [failure] })
        }
        if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method())) return fulfill(route, { code: 'UNMATCHED_MUTATION_BLOCKED' }, 409)
        return fulfill(route, [])
    })
    try {
        for (const lang of ['en', 'ar']) {
            const t = dictionaries[lang], labels = t.shippingAdministration.connection
            await page.setViewportSize({ width: 1440, height: 1000 })
            await page.goto('http://localhost:5173/ecommerce/shipping-methods')
            await page.getByRole('button', { name: /^(EN|AR)$/, exact: true }).waitFor()
            const languageToggle = page.getByRole('button', { name: lang === 'en' ? 'EN' : 'AR', exact: true })
            if (await languageToggle.count()) await languageToggle.click()
            await page.getByRole('button', { name: lang === 'en' ? 'AR' : 'EN', exact: true }).waitFor()
            await page.getByRole('tab', { name: t.shippingAdministration.tabs.carrierAccounts, exact: true }).click()
            for (const code of codes) {
                const definition = definitions.find(item => item.carrierCode === code)
                const card = page.getByRole('article').filter({ has: page.getByRole('heading', { name: definition.displayName, exact: true }) })
                const required = definition.credentialFields.filter(field => field.required)
                if (lang === 'en') {
                    const actual = await card.locator('input[required]').evaluateAll(inputs => inputs.map(input => input.name).sort())
                    if (JSON.stringify(actual) !== JSON.stringify(required.map(field => field.target).sort())) throw new Error(`Wrong required fields: ${code}`)
                }
                for (const field of required) {
                    const synthetic = field.key === 'accountEntity' ? 'CAI' : field.key === 'accountCountryCode' ? 'EG' : `synthetic-${field.key}`
                    await card.locator(`input[name="${field.target}"]`).fill(synthetic)
                    if (field.isSecret && await card.locator(`input[name="${field.target}"]`).getAttribute('type') !== 'password') throw new Error('Unmasked secret')
                }
                await card.getByRole('button', { name: t.common.save, exact: true }).click()
                await card.getByRole('status').filter({ hasText: labels.saving }).waitFor()
                await card.getByText(labels.saved, { exact: true }).waitFor()
                if (await card.locator('input[name=ApiKey]').inputValue()) throw new Error('Save did not clear secret')
                if (!await card.getByRole('button', { name: labels.test, exact: true }).isEnabled()) throw new Error('Saved test disabled')
                const request = traffic.filter(item => item.carrier === code && item.saveOnly).at(-1)
                if (!request || request.method !== 'POST') throw new Error('Save endpoint not reached')
                if (code === 'aramex' && request.extraFields.join(',') !== 'AccountPin,AccountEntity,AccountCountryCode') throw new Error('Aramex field binding mismatch')
                cases.push(`${lang}/${code}/required-fields-save-loading-confirmation-mask`)
                failure = 'INVALID_CREDENTIALS'
                await card.getByRole('button', { name: labels.test, exact: true }).click()
                await card.getByRole('status').filter({ hasText: labels.testing }).waitFor()
                await card.getByRole('alert').filter({ hasText: 'INVALID_CREDENTIALS' }).waitFor()
                if (await card.getByText(labels.authenticated, { exact: true }).count()) throw new Error('False connection success')
                cases.push(`${lang}/${code}/test-saved-loading-HTTP200-refusal-visible`)
                failure = `CREDENTIAL_${required[0].key.toUpperCase()}_MISSING`
                await card.getByRole('button', { name: labels.test, exact: true }).click()
                await card.getByRole('alert').filter({ hasText: failure }).waitFor()
                cases.push(`${lang}/${code}/normalized400-field-code`)
                failure = 'NETWORK_ERROR'
                await card.getByRole('button', { name: labels.test, exact: true }).click()
                await card.getByRole('alert').waitFor()
                cases.push(`${lang}/${code}/network-failure-no-success`)
            }
            await page.reload()
            await page.getByRole('tab', { name: t.shippingAdministration.tabs.carrierAccounts, exact: true }).click()
            for (const code of codes) {
                const card = page.getByRole('article').filter({ has: page.getByRole('heading', { name: definitions.find(item => item.carrierCode === code).displayName, exact: true }) })
                if (!await card.getByRole('button', { name: labels.test, exact: true }).isEnabled()) throw new Error('Saved state lost on reload')
                if (await card.locator('input[name=ApiKey]').inputValue()) throw new Error('Secret exposed on reload')
            }
            for (const width of [1440, 390]) {
                await page.setViewportSize({ width, height: 1000 })
                // Allow the app's responsive sidebar state/transition to settle.
                await page.waitForFunction(() => document.documentElement.scrollWidth <= innerWidth + 1, null, { timeout: 5000 })
                const layout = await page.evaluate(() => ({ width: innerWidth, scrollWidth: document.documentElement.scrollWidth, dir: document.documentElement.dir }))
                if (layout.scrollWidth > width + 1 || layout.dir !== (lang === 'ar' ? 'rtl' : 'ltr')) throw new Error(`Layout failed: ${lang}/${width}`)
                layouts.push({ lang, ...layout })
            }
            cases.push(`${lang}/all-four-reload-isolated-readiness-secret-retention`)
        }
        return { fixtureOnly: true, liveProviderSuccessesFabricated: 0, cases, layouts, traffic }
    } catch (error) {
        return { failed: true, error: error.message, cases, traffic, visible: await page.locator('main').innerText() }
    } finally {
        await page.unroute('**/api/**')
        await page.setViewportSize({ width: 1440, height: 1000 })
        await page.goto('http://localhost:5173/ecommerce/shipping-methods')
    }
}

async page => {
    // Unmocked GET/navigation/presentation checks only; no provider requests.
    const dictionaries = await page.evaluate(async () => ({ en: await (await fetch('/src/i18n/locales/en.json')).json(), ar: await (await fetch('/src/i18n/locales/ar.json')).json() }))
    const checks = []
    for (const lang of ['en', 'ar']) {
        const t = dictionaries[lang]
        await page.setViewportSize({ width: 1440, height: 1000 })
        await page.goto('http://localhost:5173/ecommerce/shipping-methods')
        await page.getByRole('button', { name: /^(EN|AR)$/, exact: true }).waitFor()
        const toggle = page.getByRole('button', { name: lang === 'en' ? 'EN' : 'AR', exact: true })
        if (await toggle.count()) await toggle.click()
        await page.getByRole('button', { name: lang === 'en' ? 'AR' : 'EN', exact: true }).waitFor()
        await page.getByRole('tab', { name: t.shippingAdministration.tabs.carrierAccounts, exact: true }).click()
        for (const name of ['Bosta', 'Aramex', 'DHL Express', 'Mylerz']) {
            const card = page.getByRole('article').filter({ has: page.getByRole('heading', { name, exact: true }) })
            await card.getByRole('heading', { name, exact: true }).waitFor()
            await card.getByText(t.shippingAdministration.account.advancedDiagnostics, { exact: true }).click()
            if ((await card.innerText()).includes('shippingAdministration.')) throw new Error(`Untranslated card: ${lang}/${name}`)
            if (await card.locator('input[type=password]').evaluateAll(inputs => inputs.some(input => input.value))) throw new Error('Saved secret exposed')
            if (name === 'Bosta') await card.getByText(t.shippingAdministration.connection.authenticated, { exact: true }).waitFor()
            if (name === 'DHL Express') await card.getByText(t.shippingAdministration.connection.errors.authentication, { exact: true }).waitFor()
            const testEnabled = await card.getByRole('button', { name: t.shippingAdministration.connection.test, exact: true }).isEnabled()
            if (testEnabled !== ['Bosta', 'DHL Express'].includes(name)) throw new Error('Cross-carrier readiness reused')
            checks.push({ lang, carrier: name, secretsEmpty: true, testEnabled, diagnosticsLocalized: true })
        }
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 1000 })
            await page.waitForFunction(() => document.documentElement.scrollWidth <= innerWidth + 1, null, { timeout: 5000 })
            const direction = await page.evaluate(() => document.documentElement.dir)
            if (direction !== (lang === 'en' ? 'ltr' : 'rtl')) throw new Error('Wrong direction')
            await page.screenshot({ path: `C:/Users/fadya/AppData/Local/Temp/opencode/carrier-ui-20261015/live-${lang}-${width}.png`, fullPage: true })
        }
    }
    await page.setViewportSize({ width: 1440, height: 1000 })
    await page.getByRole('button', { name: 'EN', exact: true }).click()
    return { unmocked: true, providerCalls: 0, checks, screenshots: 4 }
}

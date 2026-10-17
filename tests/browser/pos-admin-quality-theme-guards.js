async page => {
    const results = []
    const check = (name, passed, layer = 'hosted-editor') => { results.push({ name, passed, layer }); if (!passed) throw new Error(name) }
    await page.goto('http://localhost:5173/storefront/appearance/colors-typography')
    await page.getByLabel('Primary Color', { exact: true }).waitFor()
    const original = await (await page.request.get('http://localhost:5173/api/admin/store-config')).json()
    const publications = []
    const noPublish = async route => { publications.push(true); await route.abort('blockedbyclient') }
    let saves = 0, release = () => {}, startedResolve
    const started = new Promise(resolve => { startedResolve = resolve })
    const held = new Promise(resolve => { release = resolve })
    const failedSave = async route => { saves++; startedResolve(); await held; await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'QE controlled save rejection' }) }) }
    await page.route('**/api/admin/store-config/theme/publish', noPublish)
    await page.route('**/api/admin/store-config/theme/overrides', failedSave)
    try {
        await page.getByLabel('Primary Color', { exact: true }).fill('invalid')
        await page.evaluate(() => document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true, bubbles: true, cancelable: true })))
        check('invalid keyboard save sends no mutation', saves === 0)
        check('invalid colors expose field error and block publication', await page.locator('#appearance-primary').getAttribute('aria-invalid') === 'true' && await page.getByRole('button', { name: 'Publish Colors & Typography', exact: true }).isDisabled())
        await page.getByRole('button', { name: 'Discard', exact: true }).click()
        await page.getByLabel('Primary Color', { exact: true }).fill('#123456')
        await page.evaluate(() => {
            document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true, bubbles: true, cancelable: true }))
            document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true, bubbles: true, cancelable: true }))
        })
        await started
        check('same-tick keyboard saves issue one request', saves === 1, 'intercepted-negative-ui')
        const response = page.waitForResponse(r => r.url().endsWith('/theme/overrides') && r.request().method() === 'PUT')
        release(); await response
        await page.getByText('QE controlled save rejection', { exact: true }).waitFor()
        check('failed save retains candidate and restores retry', await page.getByLabel('Primary Color', { exact: true }).inputValue() === '#123456' && !await page.getByRole('button', { name: 'Save Draft', exact: true }).isDisabled(), 'intercepted-negative-ui')
        await page.getByRole('button', { name: 'Back', exact: true }).click()
        await page.getByRole('dialog').waitFor()
        check('dirty navigation opens guard instead of leaving', page.url().endsWith('/storefront/appearance/colors-typography'))
        await page.getByRole('dialog').getByRole('button', { name: /Keep editing/i }).click()
        await page.getByRole('button', { name: 'Discard', exact: true }).click()
        for (const language of ['en', 'ar']) {
            const toggle = page.getByRole('button', { name: language === 'ar' ? 'AR' : 'EN', exact: true })
            if (await toggle.count()) await toggle.click()
            const primary = page.locator('#appearance-primary')
            await primary.waitFor()
            for (const width of [1440, 820, 390]) {
                await page.setViewportSize({ width, height: 1100 })
                await page.waitForFunction(() => document.documentElement.scrollWidth <= window.innerWidth + 1)
                check('theme hydrated without overflow ' + language + ' ' + width, await primary.inputValue() === original.draftTheme.colors.primary)
                await page.getByRole('button', { name: 'Preview Candidate', exact: true }).click()
                check('theme local preview rendered ' + language + ' ' + width, await page.getByLabel('Appearance candidate preview').isVisible())
                await page.screenshot({ path: '.ai/evidence/pos-admin-quality-20261015/theme-' + language + '-' + width + '.png', fullPage: true })
            }
        }
        check('no publication escaped guard tests', publications.length === 0)
    } finally {
        release()
        await page.unroute('**/api/admin/store-config/theme/overrides', failedSave)
        await page.unroute('**/api/admin/store-config/theme/publish', noPublish)
        const english = page.getByRole('button', { name: 'EN', exact: true })
        if (await english.count()) await english.click()
        await page.setViewportSize({ width: 1440, height: 1000 })
    }
    const current = await (await page.request.get('http://localhost:5173/api/admin/store-config')).json()
    check('guard tests left authoritative draft/live/revision unchanged', current.rowVersion === original.rowVersion && current.themeDraftOverridesJson === original.themeDraftOverridesJson && current.themeLiveOverridesJson === original.themeLiveOverridesJson)
    const context = await page.context().browser().newContext()
    try {
        const guest = await context.newPage()
        await guest.goto('http://localhost:5173/storefront/appearance/colors-typography')
        await guest.waitForURL('**/login')
        await guest.getByRole('button', { name: /Sign In|تسجيل الدخول/ }).waitFor()
        check('direct protected URL redirects unauthenticated browser', true, 'fresh-guest-browser')
    } finally { await context.close() }
    return { results, saves: { intercepted: saves, real: 0 }, publications: 0 }
}

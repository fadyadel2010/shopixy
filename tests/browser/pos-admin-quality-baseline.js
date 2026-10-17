async page => {
    const tab = page
    const errors = [], requests = [], failures = []
    tab.on('pageerror', error => errors.push(error.message))
    tab.on('response', response => {
        if (response.url().includes('/api/')) requests.push({ path: response.url().split('/api/')[1].split('?')[0], status: response.status() })
    })
    tab.on('requestfailed', request => failures.push({ path: request.url().split('?')[0], error: request.failure()?.errorText }))
    await tab.goto('http://localhost:5173/storefront/appearance/colors-typography')
    await tab.getByLabel('Primary Color', { exact: true }).waitFor({ timeout: 45000 })
    const result = await tab.evaluate(async () => {
        const read = async path => {
            const response = await fetch('/api/' + path, { credentials: 'include' })
            return { status: response.status, data: await response.json() }
        }
        const session = await read('auth/session')
        const config = await read('admin/store-config')
        const presets = await read('admin/store-config/themes/presets')
        return {
            session: { status: session.status, role: session.data.user?.role, tenantId: session.data.user?.tenantId },
            config: { status: config.status, tenantId: config.data.tenantId, rowVersion: config.data.rowVersion,
                draftTheme: config.data.draftTheme, liveTheme: config.data.liveTheme,
                themeDraftOverridesJson: config.data.themeDraftOverridesJson, themeLiveOverridesJson: config.data.themeLiveOverridesJson },
            presets: { status: presets.status, keys: presets.data.map(item => item.key) },
            ui: { primary: document.querySelector('#appearance-primary')?.value, font: document.querySelector('#appearance-font-family')?.value },
            navigation: [...document.querySelectorAll('nav a[href]')].map(link => ({ text: link.textContent.trim(), path: link.getAttribute('href') })),
        }
    })
    if (result.config.draftTheme.colors.primary !== result.ui.primary) throw new Error('Persisted primary not hydrated')
    await tab.screenshot({ path: '.ai/evidence/pos-admin-quality-20261015/theme-baseline.png', fullPage: true })
    return { ...result, hydrated: true, errors, requests, failures }
}

async page => {
    const results = []
    const authoritative = await (await page.request.get('http://localhost:5173/api/admin/store-config')).json()
    for (const missing of ['draftTheme', 'liveTheme', 'draftColor']) {
        const incomplete = async route => {
            const body = JSON.parse(JSON.stringify(authoritative))
            if (missing === 'draftColor') delete body.draftTheme.colors.secondary
            else body[missing] = null
            await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
        }
        await page.route('**/api/admin/store-config', incomplete)
        try {
            await page.goto('http://localhost:5173/storefront/appearance/colors-typography')
            await page.getByText('Failed to load theme settings', { exact: true }).waitFor()
            if (await page.getByRole('button', { name: 'Save Draft', exact: true }).count() || await page.getByRole('button', { name: 'Publish Colors & Typography', exact: true }).count()) throw new Error('Incomplete theme admitted')
            results.push({ missing, mutationControlsAbsent: true, layer: 'intercepted-invalid-read' })
        } finally { await page.unroute('**/api/admin/store-config', incomplete) }
        await page.getByRole('button', { name: 'Try again', exact: true }).click()
        await page.getByLabel('Primary Color', { exact: true }).waitFor()
        results.push({ missing, realReadRecovery: true, layer: 'real-hosted-read' })
    }
    return { results, mutations: 0 }
}

async page => {
    await page.goto('http://localhost:5173/storefront/appearance/colors-typography')
    await page.getByLabel('Primary Color', { exact: true }).waitFor()
    const result = await page.evaluate(async () => {
        const read = async path => {
            const response = await fetch('/api/admin/store-config' + path)
            if (!response.ok) throw new Error('Read failed: ' + response.status)
            return response.json()
        }
        const before = await read(''), versionsBefore = await read('/versions')
        const sameJson = (a, b) => {
            const canonical = value => value && typeof value === 'object'
                ? Array.isArray(value) ? value.map(canonical) : Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])]))
                : value
            return JSON.stringify(canonical(a)) === JSON.stringify(canonical(b))
        }
        if (before.themePresetKey !== before.themeLivePresetKey || !sameJson(JSON.parse(before.themeDraftOverridesJson), JSON.parse(before.themeLiveOverridesJson)))
            throw new Error('Draft differs from live: no real publication admitted')
        const csrf = decodeURIComponent(document.cookie.split(';').map(p => p.trim()).find(p => p.startsWith('csrf_token='))?.slice(11) || '')
        const results = []
        for (let attempt = 0; attempt < 2; attempt++) {
            const response = await fetch('/api/admin/store-config/theme/publish', { method: 'POST',
                headers: { 'X-CSRF-Token': csrf, 'If-Match': '"' + before.rowVersion + '"' } })
            const accepted = await response.json()
            if (response.status !== 200 || accepted.rowVersion !== before.rowVersion) throw new Error('No-op publication changed authority or failed')
            results.push({ http: response.status, unchangedRevision: true })
        }
        const after = await read(''), versionsAfter = await read('/versions')
        if (after.themeLiveOverridesJson !== before.themeLiveOverridesJson || !sameJson(versionsBefore, versionsAfter)) throw new Error('No-op publish appended history or changed live')
        return { layer: 'real-restarted-api', results, historyUnchanged: true, draftAndLiveAlreadyIdentical: true, revision: before.rowVersion, contentChanged: false }
    })
    return result
}

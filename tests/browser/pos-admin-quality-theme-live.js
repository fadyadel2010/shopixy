async page => {
    const results = []
    const check = (name, passed, details = {}) => {
        results.push({ name, passed, ...details })
        if (!passed) throw new Error(name)
    }
    const read = async () => {
        const response = await page.request.get('http://localhost:5173/api/admin/store-config')
        if (response.status() !== 200) throw new Error('Config read failed')
        return response.json()
    }
    const mutate = (path, payload, rowVersion) => page.evaluate(async ({ path, payload, rowVersion }) => {
        const csrf = document.cookie.split(';').map(p => p.trim()).find(p => p.startsWith('csrf_token='))?.slice(11)
        const response = await fetch('/api/admin/store-config/' + path, {
            method: path === 'theme/overrides' ? 'PUT' : 'POST', credentials: 'include',
            headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': decodeURIComponent(csrf || ''), ...(rowVersion ? { 'If-Match': '"' + rowVersion + '"' } : {}) },
            ...(payload ? { body: JSON.stringify(payload) } : {}),
        })
        const body = await response.json()
        return { status: response.status, body }
    }, { path, payload, rowVersion })
    await page.goto('http://localhost:5173/storefront/appearance/colors-typography')
    await page.getByLabel('Primary Color', { exact: true }).waitFor()
    const original = await read()
    let ownedRevision = null, restored = false
    let interceptedPublishes = 0
    const blockPublish = async route => { interceptedPublishes++; await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'QE intercepted publication: no live content changed.' }) }) }
    const failSave = route => route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'QE controlled draft failure' }) })
    try {
        check('materialized draft hydration', await page.getByLabel('Primary Color', { exact: true }).inputValue() === original.draftTheme.colors.primary)
        const noPrecondition = await mutate('theme/publish')
        check('real API publication requires revision', noPrecondition.status === 428, { http: noPrecondition.status, code: noPrecondition.body.code })

        const nextRadius = original.draftTheme.borderRadius === 12 ? 13 : 12
        await page.getByLabel(/Border Radius/).fill(String(nextRadius))
        const savedResponse = page.waitForResponse(r => r.url().endsWith('/theme/overrides') && r.request().method() === 'PUT')
        await page.getByRole('button', { name: 'Save Draft', exact: true }).click()
        const save = await savedResponse, saved = await save.json()
        ownedRevision = saved.rowVersion
        check('real draft save accepted', save.status() === 200 && saved.draftTheme.borderRadius === nextRadius, { http: save.status() })
        await page.getByText('Appearance draft saved', { exact: true }).waitFor()
        const persisted = await read()
        check('real persisted draft changed but live did not', persisted.rowVersion === ownedRevision && persisted.draftTheme.borderRadius === nextRadius && persisted.themeLiveOverridesJson === original.themeLiveOverridesJson)
        await page.reload()
        await page.getByLabel(/Border Radius/).waitFor()
        check('reload hydrates persisted draft', await page.getByLabel(/Border Radius/).inputValue() === String(nextRadius))
        await page.getByRole('button', { name: 'Preview Candidate', exact: true }).click()
        check('preview reflects candidate radius', await page.getByLabel('Appearance candidate preview').locator('h3').evaluate(el => el.parentElement.style.borderRadius) === nextRadius + 'px')

        // These are explicitly intercepted failure guards, not real publication E2E.
        await page.route('**/api/admin/store-config/theme/publish', blockPublish)
        await page.route('**/api/admin/store-config/theme/overrides', failSave)
        await page.getByLabel('Primary Color', { exact: true }).fill('#123456')
        await page.getByRole('button', { name: 'Publish Colors & Typography', exact: true }).click()
        await page.getByRole('dialog').getByRole('button', { name: 'Publish Colors & Typography', exact: true }).click()
        await page.getByText('QE controlled draft failure', { exact: true }).waitFor()
        check('intercepted failed draft blocks publication', interceptedPublishes === 0, { layer: 'intercepted-negative-ui' })
        await page.getByRole('dialog').getByRole('button', { name: /Keep editing/i }).click()
        await page.unroute('**/api/admin/store-config/theme/overrides', failSave)
        await page.getByRole('button', { name: 'Discard', exact: true }).click()

        // Restore the exact original draft contract only if no other editor wrote it.
        const current = await read()
        check('restore does not overwrite another administrator revision', current.rowVersion === ownedRevision)
        const restore = await mutate('theme/overrides', { ...JSON.parse(original.themeDraftOverridesJson || '{}'), presetKey: original.draftTheme.presetKey }, ownedRevision)
        check('original draft restored through real API', restore.status === 200)
        ownedRevision = restore.body.rowVersion
        restored = true
        await page.reload()
        await page.getByLabel(/Border Radius/).waitFor()
        const after = await read()
        check('restored draft and unchanged live verified', JSON.stringify(after.draftTheme) === JSON.stringify(original.draftTheme) && after.themeLiveOverridesJson === original.themeLiveOverridesJson)
        await page.screenshot({ path: '.ai/evidence/pos-admin-quality-20261015/theme-live-restored.png', fullPage: true })
        return { results, livePublicationPerformed: false, restored, originalRevision: original.rowVersion, finalRevision: ownedRevision }
    } finally {
        await page.unroute('**/api/admin/store-config/theme/overrides', failSave)
        await page.unroute('**/api/admin/store-config/theme/publish', blockPublish)
        if (ownedRevision && !restored) {
            const current = await read()
            if (current.rowVersion === ownedRevision) {
                const restore = await mutate('theme/overrides', { ...JSON.parse(original.themeDraftOverridesJson || '{}'), presetKey: original.draftTheme.presetKey }, ownedRevision)
                if (restore.status !== 200) throw new Error('Original draft restoration requires attention')
            } else throw new Error('Concurrent theme edit detected; original baseline retained in browser-baseline.json, not overwritten')
        }
    }
}

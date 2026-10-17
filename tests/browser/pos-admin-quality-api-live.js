async page => {
    await page.goto('http://localhost:5173/storefront/appearance/colors-typography')
    await page.getByLabel('Primary Color', { exact: true }).waitFor()
    return page.evaluate(async () => {
        const results = []
        const check = (name, passed, details) => { results.push({ name, passed, ...details }); if (!passed) throw new Error(name) }
        const csrf = decodeURIComponent(document.cookie.split(';').map(p => p.trim()).find(p => p.startsWith('csrf_token='))?.slice(11) || '')
        const request = async (path, options = {}) => {
            const response = await fetch('/api/' + path, { credentials: 'include', ...options })
            let data; try { data = await response.json() } catch { data = null }
            return { status: response.status, data }
        }
        for (const path of ['admin/store-config', 'products', 'warehouses', 'admin/online-customers', 'admin/shipping/carrier-accounts', 'users']) {
            const response = await request(path, { credentials: 'omit' })
            // TenantResolutionMiddleware precedes authorization on localhost:
            // an anonymous request with no trusted tenant returns TENANT_REQUIRED.
            check('guest denied ' + path, response.status === 401 || (response.status === 400 && response.data?.code === 'TENANT_REQUIRED'), { http: response.status, code: response.data?.code })
        }
        const before = await request('admin/store-config')
        const spoof = await request('admin/store-config', { headers: { 'X-Tenant-Id': crypto.randomUUID() } })
        check('untrusted tenant header cannot change store authority', [400, 403].includes(spoof.status) || (spoof.status === 200 && spoof.data.tenantId === before.data.tenantId), { http: spoof.status })
        const invalid = await request('admin/store-config/theme/overrides', { method: 'PUT',
            headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf, 'If-Match': '"' + before.data.rowVersion + '"' }, body: JSON.stringify({ colors: { primary: 'invalid' } }) })
        check('real invalid theme field refused with useful validation', invalid.status === 400 && /hex color/i.test(invalid.data.message), { http: invalid.status, code: invalid.data.code })
        const stale = await request('admin/store-config/theme/overrides', { method: 'PUT',
            headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf, 'If-Match': '"0"' }, body: JSON.stringify({ colors: { primary: '#123456' } }) })
        check('stale revision cannot overwrite draft', stale.status === 409, { http: stale.status, code: stale.data.code })
        const after = await request('admin/store-config')
        check('rejected mutations left draft/live/revision intact', after.data.rowVersion === before.data.rowVersion && after.data.themeDraftOverridesJson === before.data.themeDraftOverridesJson && after.data.themeLiveOverridesJson === before.data.themeLiveOverridesJson)
        const manual = await request('admin/payments/manual-wallet/pending')
        check('manual queue reports configured capability honestly', manual.status === 200 || ['MANUAL_PAYMENT_CONFIGURATION_MISSING', 'MANUAL_PAYMENT_CONFIGURATION_INVALID', 'MANUAL_PAYMENT_PROVIDER_DISABLED'].includes(manual.data?.code), { http: manual.status, code: manual.data?.code || null })
        const whatsapp = await request('admin/whatsapp/accounts')
        check('WhatsApp account configuration is readable without dispatch', whatsapp.status === 200 && Array.isArray(whatsapp.data), { http: whatsapp.status, accounts: whatsapp.data.map(a => ({ enabled: a.enabled, status: a.status, otp: a.useForOtpVerification })) })
        const providers = await request('admin/storefront/auth-providers')
        // Contract observation only: no OAuth handshake or provider setting mutation.
        check('auth-provider settings readable without a handshake', providers.status === 200, { http: providers.status })
        return { layer: 'real-hosted-api', results, messagesSent: 0, livePublicationPerformed: false }
    })
}

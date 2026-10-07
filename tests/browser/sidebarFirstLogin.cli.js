// Execute with the existing headed named session:
// playwright-cli -s=opencode-ui-test run-code --filename=tests/browser/sidebarFirstLogin.cli.js
// Set window.__sidebarAuditStage to a descriptive checkpoint before each run.
// This checks the sidebar itself; it never navigates to either protected route.
async page => {
    const stage = await page.evaluate(() => window.__sidebarAuditStage || 'checkpoint')
    if (!/^[a-zA-Z0-9-]+$/.test(stage)) throw new Error('Invalid evidence checkpoint name')
    const authority = await page.evaluate(async () => {
        const { useAuthStore } = await import('/src/store/authStore.js')
        const state = useAuthStore.getState()
        return { user: state.user, authenticated: state.isAuthenticated, hydrated: state.hasHydrated }
    })
    if (!authority.authenticated || !authority.hydrated) throw new Error('Canonical session has not hydrated')
    const results = []
    for (const [href, permission] of [
        ['/ecommerce/payment-methods', 'storefront.payments.manage'],
        ['/ecommerce/shipping-methods', 'shipping.manage'],
    ]) {
        const allowed = authority.user.effectivePermissions.includes(permission)
        const link = page.locator(`aside a[href="${href}"]`)
        if (await link.count() !== Number(allowed)) throw new Error(`Navigation permission mismatch: ${href}`)
        if (!allowed) { results.push({ href, allowed, absent: true }); continue }
        await link.waitFor({ state: 'visible' })
        await link.scrollIntoViewIfNeeded()
        await page.waitForFunction(path => {
            const link = document.querySelector(`aside a[href="${path}"]`)
            if (!link) return false
            const box = link.getBoundingClientRect()
            const target = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2)
            if (!target || !link.contains(target)) return false
            for (let parent = link.parentElement; parent; parent = parent.parentElement) {
                if (!parent.id.startsWith('sidebar-submenu-')) continue
                if (parent.hasAttribute('inert') || getComputedStyle(parent).opacity !== '1') return false
                const clip = parent.getBoundingClientRect()
                if (box.top < clip.top - 1 || box.bottom > clip.bottom + 1) return false
            }
            return true
        }, href, { timeout: 5000 })
        results.push({ href, allowed, visible: true, hitTargetReachable: true })
        await page.screenshot({ path: `.playwright-cli/sidebar-${stage}-${href.includes('payment') ? 'payment' : 'shipping'}.png` })
    }
    const browser = await page.evaluate(() => ({
        url: location.href, lang: document.documentElement.lang, dir: document.documentElement.dir,
        viewport: { width: innerWidth, height: innerHeight },
        expansion: Object.fromEntries(Object.entries(localStorage).filter(([key]) => key.includes('sidebar'))),
        requests: performance.getEntriesByType('resource').filter(r => new URL(r.name).pathname.startsWith('/api/')).map(r => ({ url: r.name, status: r.responseStatus })),
    }))
    if (/first-session|first-login|activated/.test(stage) && browser.requests.some(r => /\/api\/(admin\/payment-methods|shippingmethods|shipping-methods|admin\/shipping)/.test(r.url)))
        throw new Error('First-login evidence already contains payment/shipping page requests')
    const evidence = { stage, authority, results, browser }
    const downloadPromise = page.waitForEvent('download')
    await page.evaluate(data => {
        const link = document.createElement('a')
        link.href = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }))
        link.download = 'sidebar-certification.json'
        link.click()
        URL.revokeObjectURL(link.href)
    }, evidence)
    await (await downloadPromise).saveAs(`.playwright-cli/sidebar-${stage}-checks.json`)
    return { stage, results, role: authority.user.role, tenantId: authority.user.tenantId, url: browser.url, lang: browser.lang, dir: browser.dir, viewport: browser.viewport, apiRequestCount: browser.requests.length }
}

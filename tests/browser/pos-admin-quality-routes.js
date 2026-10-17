async page => {
    // Real hosted reads. No business mutations or external operations are admitted.
    const routes = ['/', '/products', '/categories', '/units', '/barcode-printing', '/customers', '/suppliers',
        '/sales-representatives', '/sales-history', '/sales-returns', '/purchase-invoice', '/purchase-returns',
        '/warehouses', '/inventory', '/opening-balance/customers', '/opening-balance/suppliers', '/opening-balance/warehouse-stock',
        '/statement-of-account', '/outstanding-balances', '/payments', '/adjustments', '/supplier-card-funding',
        '/stock-transfers', '/financial-accounts', '/banks', '/expense-categories', '/expenses',
        '/reports/sales-analysis', '/reports/sales-summary', '/reports/sales-by-category', '/reports/sales-by-payment-method',
        '/reports/inventory-valuation', '/reports/daily-close', '/reports/product-movement', '/reports/ar-aging', '/reports/ap-aging',
        '/reports/sales-return-report', '/reports/purchase-return-report', '/reports/purchase-register', '/reports/purchases-by-supplier',
        '/reports/cost-analysis', '/reports/expenses', '/sales-rep-reports', '/settings', '/settings/audit-log', '/users',
        '/ecommerce/overview', '/ecommerce/orders', '/ecommerce/blocked-phones', '/ecommerce/online-customers',
        '/ecommerce/whatsapp', '/ecommerce/cart-recovery', '/ecommerce/abandoned-checkouts', '/ecommerce/payment-methods',
        '/ecommerce/manual-payments', '/ecommerce/coupons', '/ecommerce/promotions', '/ecommerce/smart-offers',
        '/ecommerce/commerce-ai', '/ecommerce/pixel-tracking', '/settings/analytics', '/ecommerce/auth-providers',
        '/analytics/funnels', '/analytics/attribution', '/analytics/attribution/models', '/analytics/attribution/orders',
        '/analytics/attribution/costs', '/analytics/attribution/roi', '/analytics/storefront', '/ecommerce/shipping-methods',
        '/storefront', '/storefront/status', '/storefront/checkout-fields', '/storefront/appearance/themes',
        '/storefront/appearance/colors-typography', '/storefront/storeconfig-version-history', '/storefront/content/pages',
        '/storefront/navigation', '/storefront/footer-content', '/storefront/domains', '/storefront/domains/list',
        '/storefront/domains/add', '/storefront/domains/activity', '/admin/shipping', '/admin/shipping/shipments',
        '/admin/shipping/health', '/admin/affiliates', '/admin/affiliates/products', '/commercehub/orders', '/admin/commercehub',
        '/commercehub/finance', '/commercehub/earnings', '/commercehub/payments', '/treasury/manual-cod-settlements',
        '/treasury/carrier-remittances', '/treasury', '/treasury/trial-balance', '/treasury/profit-loss', '/treasury/balance-sheet',
        '/treasury/cash-flow', '/treasury/general-ledger', '/treasury/clearing-aging', '/treasury/transfers', '/treasury/hub']
    const results = [], errors = [], blocked = []
    let responses = [], activeRoute = ''
    const onError = error => errors.push({ route: activeRoute, message: error.message })
    const onResponse = response => {
        if (response.url().includes('/api/')) responses.push({ path: response.url().split('/api/')[1].split('?')[0], status: response.status() })
    }
    const guard = async route => {
        const request = route.request()
        if (['GET', 'HEAD', 'OPTIONS'].includes(request.method()) || /\/api\/auth\/refresh$/.test(request.url())) return route.continue()
        blocked.push({ route: activeRoute, path: request.url().split('?')[0], method: request.method() })
        return route.abort('blockedbyclient')
    }
    page.on('pageerror', onError)
    page.on('response', onResponse)
    await page.route('**/api/**', guard)
    try {
        for (const path of routes) {
            activeRoute = path; responses = []
            try {
                await page.goto('http://localhost:5173' + path, { timeout: 30000 })
                await page.locator('main').waitFor({ timeout: 20000 })
                await page.waitForLoadState('networkidle', { timeout: 7000 }).catch(() => {})
                const state = await page.locator('main').evaluate(main => ({
                    headings: [...main.querySelectorAll('h1,h2,h3')].map(h => h.textContent.trim()).slice(0, 8),
                    alerts: [...main.querySelectorAll('[role=alert]')].map(h => h.textContent.trim()).slice(0, 5),
                    tableRows: main.querySelectorAll('tbody tr').length,
                    controls: main.querySelectorAll('button,input,select').length,
                    // Do not retain customer/financial table contents.
                    errorText: [...main.querySelectorAll('p')].map(p => p.textContent).filter(t => /failed|unavailable|error|denied|not authorized/i.test(t)).slice(0, 4),
                }))
                results.push({ path, actualPath: page.url().replace('http://localhost:5173', ''), ...state, responses: [...responses] })
            } catch (error) { results.push({ path, failure: error.message.split('\n')[0], responses: [...responses] }) }
        }
        return { layer: 'hosted-read-only-screen-survey-not-full-workflow-certification', results, errors, blocked }
    } finally {
        await page.unroute('**/api/**', guard)
        page.removeListener('pageerror', onError)
        page.removeListener('response', onResponse)
        await page.goto('http://localhost:5173/storefront/appearance/colors-typography')
    }
}

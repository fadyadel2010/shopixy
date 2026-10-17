async page => {
    const t = await page.evaluate(async () => (await fetch('/src/i18n/locales/en.json')).json())
    const results = []
    const check = (name, passed, details = {}) => { results.push({ name, passed, ...details }); if (!passed) throw new Error(name) }
    const list = await (await page.request.get('http://localhost:5173/api/admin/orders?page=1&pageSize=20')).json()
    check('order list has unique backend identities', new Set(list.items.map(o => o.orderId)).size === list.items.length, { count: list.items.length })
    if (list.items.length) {
        const id = list.items[0].orderId
        const responsePromise = page.waitForResponse(r => r.url().endsWith('/api/admin/orders/' + id))
        await page.goto('http://localhost:5173/ecommerce/orders/' + id)
        const response = await responsePromise, detail = await response.json()
        await page.getByRole('dialog').waitFor()
        check('real order detail identity matches selection', response.status() === 200 && detail.orderId === id && detail.tenantId === list.items[0].tenantId)
        check('order detail displays canonical order number', await page.getByRole('dialog').getByText(detail.orderNumber, { exact: true }).count() > 0)
    }
    await page.goto('http://localhost:5173/ecommerce/orders')
    const input = page.getByPlaceholder(t.orders.management.searchPlaceholder)
    await input.waitFor()
    const needle = 'QE-NO-SUCH-ORDER-7b463'
    const filteredPromise = page.waitForResponse(r => r.url().includes('/api/admin/orders?') && r.url().includes(needle))
    await input.fill(needle)
    const filtered = await filteredPromise, body = await filtered.json()
    await page.getByText('No orders matched the current tenant filters.', { exact: true }).waitFor()
    check('order search converges to backend empty result', filtered.status() === 200 && body.items.length === 0 && body.totalCount === 0)

    await page.goto('http://localhost:5173/inventory')
    const select = page.locator('main select').first()
    await select.locator('option').first().waitFor({ state: 'attached' })
    const warehouses = await select.locator('option').evaluateAll(options => options.map(o => ({ value: o.value, text: o.textContent })))
    for (const warehouse of warehouses.slice(0, 2)) {
        await select.selectOption(warehouse.value)
        const stockResponse = await page.request.get('http://localhost:5173/api/warehouses/' + warehouse.value + '/stock')
        const stock = await stockResponse.json()
        await page.waitForFunction(({ count, loading }) => {
            const body = document.querySelector('main tbody')
            return body && !body.textContent.includes(loading) && [...body.querySelectorAll('tr')].filter(tr => tr.querySelectorAll('td').length === 6).length === count
        }, { count: stock.length, loading: t.common.loading })
        const rows = await page.locator('main tbody tr').evaluateAll(rows => rows.filter(row => row.querySelectorAll('td').length === 6).map(row => [...row.querySelectorAll('td')].slice(3).map(cell => Number(cell.textContent.trim()))))
        const expected = stock.map(s => [s.quantity, s.reservedQuantity, s.availableQuantity].map(Number))
        check('warehouse stock rows agree with backend quantity buckets', stockResponse.status() === 200 && JSON.stringify(rows.sort()) === JSON.stringify(expected.sort()), { rows: stock.length })
    }
    const forbiddenStock = await page.request.get('http://localhost:5173/api/warehouses/00000000-0000-0000-0000-000000000001/stock')
    check('unknown warehouse does not expose unrelated stock', [400, 404].includes(forbiddenStock.status()) || (forbiddenStock.status() === 200 && (await forbiddenStock.json()).length === 0), { http: forbiddenStock.status() })
    await page.goto('http://localhost:5173/storefront/appearance/colors-typography')
    return { results, layer: 'real-hosted-read', orderOrInventoryMutations: 0 }
}

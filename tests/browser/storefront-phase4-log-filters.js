async page => {
    const checks = [], failures = [], requests = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const listener = r => { if (/\/api\/admin\/whatsapp\/logs/.test(r.url())) requests.push({ query: new URL(r.url()).search, status: r.status() }); };
    const read = path => page.evaluate(async path => (await (await import('/src/services/api.js')).default.get(path)).data, path);
    page.on('response', listener);
    try {
        await page.goto('http://localhost:5173/ecommerce/whatsapp');
        await page.locator('main').getByRole('button', { name: 'Logs', exact: true }).click();
        await page.locator('main tbody tr').first().waitFor();
        const raw = await read('/admin/whatsapp/logs?pageSize=2&page=1');
        const next = await read('/admin/whatsapp/logs?pageSize=2&page=2');
        check('Actual populated log API pagination returns distinct rows', raw.items.length === 2 && next.items.length > 0 && !raw.items.some(x => next.items.some(y => x.id === y.id)), { totalCount: raw.totalCount, page1: raw.items.length, page2: next.items.length });
        await page.locator('main select').nth(1).selectOption('OrderConfirmation');
        await page.waitForTimeout(700);
        const filtered = await read('/admin/whatsapp/logs?messageType=ORDER_CONFIRMATION');
        check('Actual log type control sends the supported wire filter', requests.some(x => x.query.includes('messageType=ORDER_CONFIRMATION') && x.status === 200));
        check('Filtered API and UI show known localized message type', filtered.items.every(x => x.messageType === 'ORDER_CONFIRMATION') && filtered.items.length > 0 && (await page.locator('main tbody').innerText()).includes('Order Confirmation') && !(await page.locator('main tbody').innerText()).includes('Unknown'));
        await page.locator('main').getByRole('button', { name: 'View', exact: true }).first().click();
        await page.getByRole('dialog').waitFor();
        check('Actual log summary dialog shows mapped recipient/type/status rather than unknown values', !(await page.getByRole('dialog').innerText()).includes('Unknown'));
        await page.getByRole('dialog').getByRole('button', { name: 'Close', exact: true }).click();
        await page.locator('main select').first().selectOption('Failed'); await page.waitForTimeout(700);
        check('Empty filtered logs are a successful read, not an unavailable response', requests.some(x => x.query.includes('status=FAILED') && x.status === 200) && (await page.locator('main tbody tr').count()) === 1);
    } catch (e) { failures.push(e.message.split('\n')[0]); }
    finally { page.off('response', listener); }
    return { checks, failures, requests };
}

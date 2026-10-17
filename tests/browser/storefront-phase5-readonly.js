async page => {
    const checks = [], failures = [], errors = [], writes = [], requests = [];
    const paths = ['/ecommerce/payment-methods', '/ecommerce/manual-payments', '/ecommerce/shipping-methods', '/admin/shipping/shipments'];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const viewport = page.viewportSize();
    const error = e => errors.push(e.message.split('\n')[0]);
    const response = r => { if (/\/api\//.test(r.url())) requests.push({ path: r.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0], method: r.request().method(), status: r.status() }); };
    const guard = route => {
        const r = route.request();
        if (/\/api\/(admin\/(payment-methods|payments\/manual-wallet|shipments|shipping)|shippingmethods|shipments)/.test(r.url()) && !['GET', 'HEAD', 'OPTIONS'].includes(r.method())) {
            writes.push({ path: r.url().split('?')[0], method: r.method() }); return route.abort();
        }
        return route.continue();
    };
    page.on('pageerror', error); page.on('response', response); await page.route('**/api/**', guard);
    let anonymous;
    try {
        anonymous = await page.context().browser().newContext();
        for (const path of ['/api/admin/payment-methods', '/api/admin/payments/manual-wallet/pending', '/api/shippingmethods', '/api/admin/shipments']) {
            const r = await anonymous.request.get('http://localhost:5010' + path, { headers: { Host: 'ahramstore.localhost:5010' } });
            check('Anonymous Phase 5 request denied ' + path, r.status() === 401, { status: r.status() });
        }
        await page.goto('http://localhost:5173/ecommerce/payment-methods'); await page.locator('main h1').waitFor();
        const spoof = await page.evaluate(async () => (await fetch('/api/admin/payment-methods', { headers: { 'X-Tenant-Id': '11111111-1111-4111-8111-111111111111' } })).status);
        check('Authenticated tenant spoof denied', spoof === 403, { status: spoof });
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 });
            for (const language of ['en', 'ar']) {
                const toggle = page.getByRole('button', { name: language === 'ar' ? 'AR' : 'EN', exact: true });
                if (await toggle.count()) await toggle.click();
                for (const path of paths) {
                    await page.goto('http://localhost:5173' + path);
                    await page.locator('main h1,main h2').first().waitFor();
                    await page.waitForTimeout(2200);
                    const text = await page.locator('main').innerText();
                    const layout = await page.evaluate(() => ({ dir: document.documentElement.dir, width: innerWidth, scroll: document.documentElement.scrollWidth }));
                    check(`${language}/${width} actual Phase 5 screen ${path}`, text.length > 30 && !/Workspace admission failed|undefined|phase5\./.test(text), { heading: await page.locator('main h1,main h2').first().innerText() });
                    check(`${language}/${width} bounded RTL/LTR ${path}`, layout.dir === (language === 'ar' ? 'rtl' : 'ltr') && layout.scroll <= width + 1, layout);
                    if (width === 390 && language === 'ar') await page.screenshot({ path: '.ai/evidence/storefront-phase5-20261017/' + path.split('/').pop() + '-ar-mobile.png' });
                }
            }
        }
        check('No Phase 5 writes in readonly browser pass', writes.length === 0);
        check('No uncaught exceptions in readonly browser pass', errors.length === 0);
    } catch (e) { failures.push(e.message.split('\n')[0]); }
    finally {
        if (anonymous) await anonymous.close();
        await page.unroute('**/api/**', guard); page.off('pageerror', error); page.off('response', response);
        if (viewport) await page.setViewportSize(viewport);
        const english = page.getByRole('button', { name: 'EN', exact: true }); if (await english.count()) await english.click();
    }
    return { checks, failures, errors, writes, requests };
}

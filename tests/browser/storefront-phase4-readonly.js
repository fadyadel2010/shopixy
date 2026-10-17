async page => {
    const checks = [], failures = [], errors = [], writes = [], screenshots = [];
    const paths = ['overview', 'orders', 'abandoned-checkouts', 'cart-recovery', 'blocked-phones', 'online-customers', 'whatsapp'];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const viewport = page.viewportSize();
    const onError = e => errors.push(e.message);
    const guard = route => {
        const r = route.request();
        if (/\/api\/(admin\/(orders|abandoned-checkouts|phones|online-customers|ecommerce\/abandoned-cart-recovery|whatsapp)|orders\/|payments\/lifecycle)/.test(r.url()) && !['GET', 'HEAD', 'OPTIONS'].includes(r.method())) {
            writes.push({ path: r.url().split('?')[0], method: r.method() }); return route.abort();
        }
        return route.continue();
    };
    page.on('pageerror', onError); await page.route('**/api/**', guard);
    let anonymous;
    try {
        anonymous = await page.context().browser().newContext();
        for (const path of ['/api/admin/orders', '/api/admin/abandoned-checkouts', '/api/admin/phones', '/api/admin/online-customers', '/api/admin/ecommerce/abandoned-cart-recovery/settings', '/api/admin/whatsapp/accounts']) {
            const r = await anonymous.request.get('http://localhost:5010' + path, { headers: { Host: 'ahramstore.localhost:5010' } });
            check('Cookie-free request denied: ' + path, r.status() === 401, { status: r.status() });
        }
        await page.goto('http://localhost:5173/ecommerce/overview'); await page.locator('main h1').waitFor();
        const spoof = await page.evaluate(async () => (await fetch('/api/admin/orders', { headers: { 'X-Tenant-Id': '11111111-1111-4111-8111-111111111111' } })).status);
        check('Authenticated tenant header spoof denied', spoof === 403, { status: spoof });
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 });
            for (const language of ['en', 'ar']) {
                const button = page.getByRole('button', { name: language === 'ar' ? 'AR' : 'EN', exact: true });
                if (await button.count()) await button.click();
                for (const path of paths) {
                    await page.goto('http://localhost:5173/ecommerce/' + path);
                    await page.locator('main h1').first().waitFor(); await page.waitForTimeout(650);
                    const text = await page.locator('main').innerText();
                    const layout = await page.evaluate(() => ({ dir: document.documentElement.dir, width: innerWidth, scroll: document.documentElement.scrollWidth }));
                    check(`${language}/${width} actual screen: ${path}`, text.length > 30 && !/Workspace admission failed|VALIDATION_ERROR|undefined/.test(text), { heading: await page.locator('main h1').first().innerText(), ...layout });
                    check(`${language}/${width} direction and bounded page: ${path}`, layout.dir === (language === 'ar' ? 'rtl' : 'ltr') && layout.scroll <= width + 1);
                    if (path === 'overview') {
                        const links = await page.locator('main .operations-overview .grid a').count();
                        check(`Overview remains navigation, not invented aggregate metrics (${language}/${width})`, links === 6);
                    }
                    if (width === 390 && language === 'ar' && ['orders', 'whatsapp'].includes(path)) {
                        const filename = '.ai/evidence/storefront-phase4-20261016/' + path + '-ar-mobile.png';
                        await page.screenshot({ path: filename }); screenshots.push(filename);
                    }
                }
            }
        }
        check('No domain authoring writes during readonly pass', writes.length === 0);
        check('No uncaught page exceptions during readonly pass', errors.length === 0);
    } catch (e) { failures.push(e.message.split('\n')[0]); }
    finally {
        if (anonymous) await anonymous.close();
        await page.unroute('**/api/**', guard); page.off('pageerror', onError);
        if (viewport) await page.setViewportSize(viewport);
        const english = page.getByRole('button', { name: 'EN', exact: true }); if (await english.count()) await english.click();
    }
    return { observedUtc: new Date().toISOString(), checks, failures, errors, writes, screenshots };
}

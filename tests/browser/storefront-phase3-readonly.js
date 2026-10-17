async page => {
    const checks = [], failures = [], pageErrors = [], writes = [];
    const check = (name, value, detail = {}) => { checks.push({ name, passed: !!value, ...detail }); if (!value) throw new Error(name); };
    const onError = e => pageErrors.push(e.message);
    const guard = route => { const request = route.request(); if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method()) && /\/api\/(coupons|promotions|admin\/smart-offers)/.test(request.url())) { writes.push({ method: request.method() }); return route.abort(); } return route.continue(); };
    page.on('pageerror', onError); await page.route('**/api/**', guard);
    const viewport = page.viewportSize();
    let anonymous;
    try {
        anonymous = await page.context().browser().newContext();
        for (const path of ['/api/coupons', '/api/promotions', '/api/admin/smart-offers']) {
            const response = await anonymous.request.get('http://localhost:5010' + path, { headers: { Host: 'ahramstore.localhost:5010' } });
            check('Cookie-free caller cannot read ' + path, [400, 401].includes(response.status()), { status: response.status() });
        }
        const spoof = await page.evaluate(async () => (await fetch('/api/coupons', { headers: { 'X-Tenant-Id': '11111111-1111-4111-8111-111111111111' } })).status);
        check('Actual authenticated foreign-tenant spoof is rejected', spoof === 403, { status: spoof });
        for (const language of ['en', 'ar']) {
            const languageButton = page.getByRole('button', { name: language === 'ar' ? 'AR' : 'EN', exact: true });
            if (await languageButton.count()) await languageButton.click();
            for (const [path, heading] of [['coupons', language === 'ar' ? 'الكوبونات' : 'Coupons'], ['promotions', null], ['smart-offers', null]]) {
                await page.goto('http://localhost:5173/ecommerce/' + path);
                await page.locator('main h1').waitFor();
                const text = await page.locator('main').innerText();
                check(`Hosted ${language} screen reads cleanly: ${path}`, text.length > 40 && !/failed to load|VALIDATION_ERROR|Workspace admission failed/i.test(text), { heading: await page.locator('main h1').innerText() });
                if (heading && language === 'en') check('Coupon heading matches actual route', (await page.locator('main h1').innerText()) === heading);
                if (language === 'ar') check(`RTL is active for ${path}`, await page.evaluate(() => document.documentElement.dir === 'rtl'));
                const next = page.locator('main').getByRole('button', { name: language === 'ar' ? /التالي/ : /^Next$/ });
                if (await next.count()) check(`Empty ${path} pagination has no phantom next page`, await next.isDisabled());
                if (path === 'coupons' && language === 'en') {
                    await page.getByRole('button', { name: '+ Add Coupon', exact: true }).click();
                    const form = page.locator('form').filter({ has: page.getByRole('textbox', { name: 'Code', exact: true }) });
                    check('Empty coupon form has accessible required code and blocks submission', !await form.evaluate(el => el.checkValidity()) && await form.getByRole('textbox', { name: 'Code', exact: true }).count() === 1);
                    await form.getByRole('textbox', { name: 'Code', exact: true }).fill('P3QA-NOT-SAVED');
                    await form.locator('input[type=number]').first().fill('101');
                    check('Coupon percentage UI refuses above 100', !await form.evaluate(el => el.checkValidity()));
                    await form.getByRole('button', { name: 'Cancel', exact: true }).click();
                }
            }
        }
        check('Readonly/invalid-input browser pass makes no authoring writes', writes.length === 0);
        check('No uncaught browser exceptions', pageErrors.length === 0);
    } catch (error) { failures.push(error.message.split('\n')[0]); }
    finally {
        if (anonymous) await anonymous.close();
        await page.unroute('**/api/**', guard); page.removeListener('pageerror', onError);
        if (viewport) await page.setViewportSize(viewport);
        const english = page.getByRole('button', { name: 'EN', exact: true }); if (await english.count()) await english.click();
    }
    return { observedUtc: new Date().toISOString(), evidence: 'hosted-readonly-actual-Admin', checks, failures, writes, pageErrors };
}

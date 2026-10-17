// Actual Owner session, API and configured database. No content mutations.
async page => {
    const checks = [], failures = [], pageErrors = [], responses = [], blockedWrites = [];
    const tenantId = 'c9c5c1ae-949e-498e-bc63-07242daeb49d';
    const originalViewport = page.viewportSize();
    const check = (name, passed, detail = {}) => { checks.push({ name, passed: Boolean(passed), ...detail }); if (!passed) throw new Error(name); };
    const onError = error => pageErrors.push(error.message);
    const onResponse = response => { const path = response.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0]; if (path.startsWith('/api/admin/') && /store-config|\/pages|\/cms\/|\/footer/.test(path)) responses.push({ path, status: response.status() }); };
    const guard = route => {
        const method = route.request().method(), path = route.request().url().replace(/^https?:\/\/[^/]+/, '').split('?')[0];
        if (path.startsWith('/api/admin/') && !['GET', 'HEAD', 'OPTIONS'].includes(method)) { blockedWrites.push({ path, method }); return route.abort('blockedbyclient'); }
        return route.continue();
    };
    page.on('pageerror', onError); page.on('response', onResponse);
    await page.route('**/api/admin/**', guard);
    let publicPage, anonymous, before, after;
    const snapshot = () => page.evaluate(async () => {
        const result = {};
        for (const [key, path] of Object.entries({ config: '/api/admin/store-config', pages: '/api/admin/pages', navigation: '/api/admin/cms/navigation', targets: '/api/admin/cms/navigation/targets', footer: '/api/admin/footer', history: '/api/admin/store-config/versions' })) {
            const response = await fetch(path, { cache: 'no-store' });
            if (!response.ok) throw new Error(`${key} read returned ${response.status}`);
            result[key] = await response.json();
        }
        return result;
    });
    const publicRead = async path => {
        const response = await page.request.get('http://localhost:5174/api/storefront/' + path, { headers: { Host: 'ahramstore.localhost:5174', 'Cache-Control': 'no-store' } });
        return { status: response.status(), body: await response.json() };
    };
    try {
        await page.goto('http://localhost:5173/storefront/content/pages');
        await page.getByRole('heading', { name: 'Pages', exact: true }).waitFor();
        before = await snapshot();
        check('Hosted Owner reads are admitted for the actual tenant', before.config.tenantId === tenantId && before.config.storeName === 'AhramStore', { tenantId, rowVersion: before.config.rowVersion });
        for (const [path, name] of [
            ['/storefront/homepage', '^Homepage$'], ['/storefront/catalog/product-template', '^Product Template'],
            ['/storefront/content/pages', '^Pages$'], ['/storefront/navigation?view=header', '^Navigation$'],
            ['/storefront/navigation?view=footer', '^Navigation$'], ['/storefront/footer-content', '^Footer Content$'],
            ['/storefront/appearance/themes', '^Themes$'], ['/storefront/appearance/colors-typography', '^Colors & Typography$'],
            ['/storefront/storeconfig-version-history', '^StoreConfig History$'],
        ]) {
            try {
                await page.goto('http://localhost:5173' + path);
                await page.getByRole('heading', { name: new RegExp(name) }).first().waitFor();
                const text = await page.locator('main').innerText();
                check('Hosted screen reads persisted authority: ' + path, !/Workspace admission failed|Themes unavailable|History unavailable|No editable fallback/.test(text), { headings: await page.locator('main').getByRole('heading').allTextContents() });
            } catch (error) { failures.push({ path, error: error.message.split('\n')[0], main: (await page.locator('main').innerText()).slice(0, 1200) }); }
        }
        const rollback = page.getByRole('button', { name: 'Rollback', exact: true }).first();
        if (await rollback.count()) {
            await rollback.click();
            const dialog = page.getByRole('dialog', { name: 'Confirm Rollback' });
            check('Hosted historical restore is gated and explains excluded surfaces', await dialog.getByRole('button', { name: 'Rollback Now' }).isDisabled() && (await dialog.innerText()).includes('Current drafts, CMS Pages, and separate Footer Content are preserved'));
            await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
        }
        const editable = before.pages.find(p => p.id);
        if (editable) {
            await page.goto('http://localhost:5173/storefront/content/pages/' + editable.id);
            await page.getByRole('button', { name: 'Save Draft', exact: true }).waitFor();
            check('Hosted CMS record editor admits the real page without creating a replacement', !await page.getByText('Page not found', { exact: true }).count(), { pageId: editable.id });
        }
        const pubConfig = await publicRead('store-config');
        check('Public StoreConfig resolves the same tenant and only published Homepage composition', pubConfig.status === 200 && pubConfig.body.tenantId === tenantId && JSON.stringify(pubConfig.body.pages?.home) === JSON.stringify(before.config.livePages?.home), { status: pubConfig.status, liveVersion: before.history.currentVersion });
        const pubNav = await publicRead('navigation');
        const pubFooter = await publicRead('footer');
        check('Public Navigation and independent Footer endpoints return tenant content', pubNav.status === 200 && pubFooter.status === 200, { navigationStatus: pubNav.status, footerStatus: pubFooter.status, footerSource: pubFooter.body.footerSource, footerSections: pubFooter.body.sections?.length });
        const liveTarget = before.targets.cmsPages.find(p => p.state === 'published' && /^\/[a-z0-9-]+$/.test(p.resolvedPath));
        publicPage = await page.context().newPage(); publicPage.on('pageerror', onError);
        await publicPage.goto('http://ahramstore.localhost:5174/');
        await publicPage.locator('main').waitFor();
        check('Hosted public Homepage renders a visible main surface', (await publicPage.locator('main').innerText()).trim().length > 0);
        await publicPage.goto('http://ahramstore.localhost:5174/products');
        await publicPage.locator('main').waitFor();
        check('Hosted catalog display is the real public products consumer', (await publicPage.locator('main').innerText()).trim().length > 0);
        if (liveTarget) {
            const livePage = await publicRead('pages' + liveTarget.resolvedPath);
            check('Published CMS target resolves through the actual public API', livePage.status === 200 && livePage.body.slug === liveTarget.resolvedPath.slice(1), { status: livePage.status, path: liveTarget.resolvedPath, language: livePage.body.language });
            await publicPage.goto('http://ahramstore.localhost:5174' + liveTarget.resolvedPath);
            await publicPage.getByRole('heading', { name: livePage.body.title, exact: true }).waitFor();
            check('Hosted CMS browser renders the API published title', true);
            await publicPage.setViewportSize({ width: 390, height: 844 });
            await publicPage.goto('http://ahramstore.localhost:5174' + liveTarget.resolvedPath + '?lang=ar');
            await publicPage.locator('[lang="ar"][dir="rtl"]').first().waitFor();
            check('Hosted CMS supports Arabic/RTL without 390px page overflow', await publicPage.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
        }
        anonymous = await page.context().browser().newContext();
        for (const path of ['admin/pages', 'admin/cms/navigation', 'admin/footer', 'admin/store-config/versions']) {
            const response = await anonymous.request.get('http://localhost:5010/api/' + path, { headers: { 'X-Tenant-Id': tenantId } });
            check('Anonymous raw tenant header cannot read ' + path, response.status() === 400 || response.status() === 401, { status: response.status() });
        }
        const spoof = await page.evaluate(async () => (await fetch('/api/admin/footer', { headers: { 'X-Tenant-Id': '11111111-1111-4111-8111-111111111111' } })).status);
        check('Authenticated foreign tenant spoof is denied', spoof === 403, { status: spoof });
        after = await snapshot();
        for (const key of ['config', 'pages', 'navigation', 'footer', 'history'])
            check('Read-only hosted pass preserves merchant ' + key, JSON.stringify(before[key]) === JSON.stringify(after[key]));
        check('No authoring mutation was attempted or forwarded', blockedWrites.length === 0);
        check('Hosted pass has no uncaught browser errors', pageErrors.length === 0, { pageErrors });
    } catch (error) { failures.push({ error: error.message.split('\n')[0] }); }
    finally {
        if (publicPage) await publicPage.close(); if (anonymous) await anonymous.close();
        await page.unroute('**/api/admin/**', guard);
        page.removeListener('pageerror', onError); page.removeListener('response', onResponse);
        if (originalViewport) await page.setViewportSize(originalViewport);
    }
    return { observedUtc: new Date().toISOString(), evidence: 'hosted-read-only-actual-api-and-database', checks, failures, pageErrors, responses, blockedWrites, merchantWrites: 0 };
}

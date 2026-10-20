// Current hosted cross-domain readback. No authoring, cart/order, chat or OAuth action.
async page => {
    const checks = [], failures = [], errors = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const publicPage = await page.context().newPage();
    // A localhost APIRequestContext in the signed-in browser carries Admin
    // cookies even when Host is overridden. Host-only probes need no cookies.
    const anonymous = await page.context().browser().newContext();
    publicPage.on('pageerror', e => errors.push(e.message.split('\n')[0]));
    const readAdmin = path => admin.evaluate(async path => { const api = (await import('/src/services/api.js')).default; const r = await api.get(path); return r.data; }, path);
    const readPublic = async path => {
        const r = await anonymous.request.get('http://localhost:5174/api/storefront/' + path, { headers: { Host: 'ahramstore.localhost:5174' } });
        return { status: r.status(), data: await r.json(), cache: r.headers()['cache-control'] };
    };
    try {
        const original = await readAdmin('/admin/store-config');
        const history = await readAdmin('/admin/store-config/versions');
        const targets = await readAdmin('/admin/cms/navigation/targets');
        const live = await readPublic('store-config');
        check('Host resolves current SQL tenant and published home, not draft', live.status === 200 && live.data.tenantId === original.tenantId && original.tenantId === 'c9c5c1ae-949e-498e-bc63-07242daeb49d' && JSON.stringify(live.data.pages?.home) === JSON.stringify(original.livePages?.home), { tenantId: original.tenantId, liveVersion: history.currentVersion, reviewedRevision: original.rowVersion });
        check('Public config is centrally no-store', /no-store/.test(live.cache));
        check('Current Commercial AI is disabled without an inference request', original.commerceAi.enabled === false && live.data.commerceAiEnabled === false);
        const definitions = await readAdmin('/admin/storefront/checkout-fields');
        const publicFields = await readPublic('checkout-fields');
        const expected = definitions.fields.filter(x => x.isEnabled && !x.isDeleted).map(x => x.id).sort();
        check('Public checkout fields match accepted enabled Admin definitions', publicFields.status === 200 && JSON.stringify(publicFields.data.fields.map(x => x.id).sort()) === JSON.stringify(expected), { adminActiveCount: definitions.fields.length, publicCount: publicFields.data?.fields?.length });
        for (const name of ['navigation', 'footer']) {
            const r = await readPublic(name);
            check('Public SQL consumer available: ' + name, r.status === 200 && /no-store/.test(r.cache), { footerSource: r.data.footerSource, sections: r.data.sections?.length });
        }
        const products = await readPublic('products/discover?pageSize=20');
        check('Real published discovery has supported products', products.status === 200 && products.data.items.length > 0, { count: products.data.items?.length });
        await publicPage.goto('http://ahramstore.localhost:5174/', { waitUntil: 'domcontentloaded' });
        await publicPage.locator('main').waitFor();
        check('Published home renders without experimental publication/theme change', (await publicPage.locator('main').innerText()).trim().length > 0);
        const product = products.data.items.find(x => x.slug === 'cert-media-simple-20261007');
        await publicPage.goto('http://ahramstore.localhost:5174/products/' + product.slug, { waitUntil: 'domcontentloaded' });
        await publicPage.getByRole('heading', { name: product.name, exact: true }).first().waitFor();
        check('Product template renders runtime-owned product authority', !(await publicPage.locator('main').innerText()).includes('Unknown section'));
        const target = targets.cmsPages.find(x => x.state === 'published' && /^\/[a-z0-9-]+$/.test(x.resolvedPath));
        check('Published CMS navigation target exists', !!target);
        const cms = await readPublic('pages' + target.resolvedPath);
        await publicPage.goto('http://ahramstore.localhost:5174' + target.resolvedPath, { waitUntil: 'domcontentloaded' });
        await publicPage.getByRole('heading', { name: cms.data.title, exact: true }).waitFor();
        check('Published CMS API, navigation identity and browser title agree', cms.status === 200 && cms.data.slug === target.resolvedPath.slice(1));
        const domains = await readAdmin('/admin/storefront/domains?pageSize=20');
        check('Saved-domain portfolio is read separately from DNS/TLS readiness', Array.isArray(domains.items), { savedCount: domains.totalCount ?? domains.items.length, externalDnsTlsVerified: false });
        const providers = await readPublic('auth/providers');
        check('No unconfigured OAuth provider is publicly selectable', providers.status === 200 && !providers.data.google && !providers.data.facebook && !('clientSecret' in providers.data));
        const settings = await readAdmin('/admin/analytics/settings');
        check('Advertising providers remain unconfigured/disabled', !settings.google?.enabled && !settings.meta?.enabled && !settings.tikTok?.enabled);
        const foreign = await anonymous.request.get('http://localhost:5174/api/storefront/store-config', { headers: { Host: 'ahramstore.localhost:5174', 'X-Tenant-Id': '11111111-1111-4111-8111-111111111111' } });
        check('Foreign tenant header cannot replace host authority', foreign.status() === 403);
        const unknown = await anonymous.request.get('http://localhost:5174/api/storefront/store-config', { headers: { Host: 'phase8-unknown.localhost:5174' } });
        check('Unknown local tenant hostname fails closed', [400, 404].includes(unknown.status()), { status: unknown.status() });
        const after = await readAdmin('/admin/store-config');
        check('Entire Admin StoreConfig unchanged across hosted read-only pass', JSON.stringify(original) === JSON.stringify(after));
        check('No uncaught errors in current public content consumers', errors.length === 0);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally { await publicPage.close(); await anonymous.close(); }
    return { observedUtc: new Date().toISOString(), evidence: 'hosted UI + independent current API readback', checks, failures, errors, mutations: 0, modelRequests: 0, genuineOAuthSignIns: 0 };
}

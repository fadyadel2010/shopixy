// Public source consumers with intercepted API responses, not hosted publication.
async page => {
    const checks = [], failures = [], pageErrors = [], requests = [];
    const check = (name, passed) => { checks.push({ name, passed: Boolean(passed) }); if (!passed) throw new Error(name); };
    const context = await page.context().browser().newContext({ viewport: { width: 1440, height: 1000 } });
    const tab = await context.newPage(); tab.on('pageerror', e => pageErrors.push(e.message));
    const cms = { slug: 'about', title: 'Synthetic public CMS', contentType: 'RichText', content: '<p>Safe published content</p><script>window.phase2Xss=true</script><img src="x" onerror="window.phase2Xss=true">', schemaVersion: 1, language: 'en', requestedLanguage: 'en', fallbackApplied: false, availableLanguages: ['en', 'ar'], canonicalPath: '/about', alternatePaths: { en: '/about?lang=en', ar: '/about?lang=ar' }, seo: { metaTitle: 'Synthetic CMS SEO' }, updatedAtUtc: new Date().toISOString() };
    await context.route('**/__phase2_public__', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><body><div id="fixture"></div></body></html>' }));
    await context.route('**/api/**', route => {
        const url = route.request().url(); const path = url.replace(/^https?:\/\/[^/]+/, '').split('?')[0];
        if (!path.startsWith('/api/')) return route.continue();
        requests.push({ method: route.request().method(), path });
        const ar = url.includes('lang=ar') || route.request().headers()['accept-language']?.startsWith('ar');
        const json = (value, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) });
        if (path.endsWith('/navigation')) return json({ header: { links: [] }, footer: { links: [{ label: 'Published navigation', labelAr: 'التنقل المنشور', type: 'cms', slug: 'about', openInNewTab: false }] }, updatedAtUtc: new Date().toISOString() });
        if (path.endsWith('/footer')) return json({ footerSource: 'Tenant', socialSource: 'None', sections: [{ title: ar ? 'خدمة العملاء' : 'Published Footer', sortOrder: 0, links: [{ label: ar ? 'حول المتجر' : 'Published CMS link', type: 'Cms', url: '/about', openInNewTab: false }] }], socialLinks: [], generatedAtUtc: new Date().toISOString() });
        if (path.endsWith('/about')) return json(ar ? { ...cms, title: 'صفحة المتجر', language: 'ar', requestedLanguage: 'ar', content: '<p>المحتوى المنشور الآمن</p>' } : cms);
        return json({ message: 'Controlled missing page' }, 404);
    });
    try {
        await tab.goto('http://localhost:5174/__phase2_public__');
        await tab.evaluate(async () => {
            const refresh = await import('/@react-refresh'); refresh.injectIntoGlobalHook(window);
            window.$RefreshReg$ = () => {}; window.$RefreshSig$ = () => type => type;
            const source = await (await fetch('/src/main.tsx')).text();
            const reactPath = source.match(/from "([^"]*\/react\.js[^"]*)"/)[1];
            const clientPath = source.match(/from "([^"]*react-dom_client[^"]*)"/)[1];
            const reactModule = await import(reactPath), clientModule = await import(clientPath);
            const React = reactModule.default || reactModule;
            const createRoot = clientModule.createRoot || clientModule.default.createRoot;
            await import('/src/styles/index.css');
            const { CmsPageView } = await import('/src/components/cms/CmsPage.tsx');
            const { FooterRuntime } = await import('/src/storefront/runtime/layout/LayoutRuntime.tsx');
            const { Footer } = await import('/src/components/layout/Footer.tsx');
            const { SectionRenderer } = await import('/src/components/sections/SectionRenderer.tsx');
            const sectionSource = await (await fetch('/src/components/sections/SectionRenderer.tsx')).text();
            const { ThemeProvider } = await import(sectionSource.match(/"([^" ]*\/src\/storefront\/themes\/ThemeContext\.tsx[^" ]*)"/)[1]);
            const { useStoreConfigStore } = await import('/src/store/storeConfigStore.ts');
            const { DEFAULT_STORE_CONFIG } = await import('/src/types/storeConfig.ts');
            const { useLanguageStore } = await import('/src/i18n/languageStore.ts');
            const { setResolvedTenant } = await import('/src/bootstrap/tenantResolver.ts');
            const runtimeSource = await (await fetch('/src/storefront/runtime/layout/LayoutRuntime.tsx')).text();
            const navPath = runtimeSource.match(/"([^" ]*\/src\/stores\/navigationStore\.ts[^" ]*)"/)[1];
            const { useNavigationStore } = await import(navPath);
            const { getSectionDefaultSettings } = await import('/src/storefront/runtime/sectionContract.ts');
            const editorialSettings = { ...getSectionDefaultSettings('text-block'), heading: 'Published homepage heading', body: 'Published homepage editorial' };
            const { MemoryRouter } = await import(runtimeSource.match(/from "([^"]*react-router-dom[^"]*)"/)[1]);
            setResolvedTenant('22222222-2222-4222-8222-222222222222', { overwrite: true });
            useStoreConfigStore.getState().setRuntimeConfig({ ...DEFAULT_STORE_CONFIG, tenantId: '22222222-2222-4222-8222-222222222222', storeName: 'Synthetic Public Store', storefrontTheme: 'modern' });
            useLanguageStore.getState().setLanguage('en');
            await useNavigationStore.getState().loadNavigation();
            const root = createRoot(document.getElementById('fixture'));
            window.phase2PublicRender = slug => root.render(React.createElement(MemoryRouter, null, React.createElement(ThemeProvider, null,
                React.createElement(SectionRenderer, { sections: [{ id: 'editorial', type: 'text-block', enabled: true, order: 0, settings: editorialSettings }, { id: 'disabled', type: 'text-block', enabled: false, order: 1, settings: { ...editorialSettings, body: 'Should not be shown' } }] }),
                React.createElement(CmsPageView, { slug }), React.createElement(FooterRuntime, { component: Footer }))));
            window.phase2Language = useLanguageStore;
            window.phase2PublicRender('about');
        });
        await tab.getByRole('heading', { name: 'Synthetic public CMS', exact: true }).waitFor();
        await tab.getByRole('link', { name: 'Published CMS link', exact: true }).waitFor();
        check('Public CMS consumer renders intercepted content and SEO', await tab.title() === 'Synthetic CMS SEO');
        check('Public CMS sanitization strips executable markup', await tab.evaluate(() => !window.phase2Xss && document.querySelector('article script') === null && document.querySelector('article [onerror]') === null));
        check('Footer runtime consumes API Footer Content and Navigation independently', await tab.getByRole('link', { name: 'Published navigation', exact: true }).getAttribute('href') === '/about');
        check('Footer CMS route is the canonical published route', await tab.getByRole('link', { name: 'Published CMS link', exact: true }).getAttribute('href') === '/about');
        await tab.getByText('Published homepage editorial', { exact: true }).waitFor();
        check('Public section renderer displays enabled editorial content and suppresses disabled sections', await tab.getByText('Should not be shown', { exact: true }).count() === 0);
        await tab.setViewportSize({ width: 390, height: 844 });
        await tab.evaluate(() => window.phase2Language.getState().setLanguage('ar'));
        await tab.getByRole('heading', { name: 'صفحة المتجر', exact: true }).waitFor();
        await tab.getByRole('link', { name: 'حول المتجر', exact: true }).waitFor();
        check('Arabic CMS reload is RTL and Footer uses the locale-specific payload', await tab.locator('[lang="ar"][dir="rtl"]').count() > 0);
        check('390px public CMS/Footer fixture has no page-wide horizontal overflow', await tab.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1));
        await tab.evaluate(() => window.phase2PublicRender('not-found'));
        await tab.getByRole('heading', { name: /غير موجود|لم يتم العثور|not found/i }).waitFor();
        check('Missing CMS read does not retain the previous page as if it were current', await tab.getByRole('heading', { name: 'صفحة المتجر', exact: true }).count() === 0);
    } catch (error) { failures.push({ error: error.message.split('\n')[0], body: (await tab.locator('body').innerText()).slice(0, 3000) }); }
    finally { await context.close(); }
    return { evidence: 'controlled-public-source-with-intercepted-http', checks, failures, pageErrors, requests, realApplicationWrites: 0 };
}

// Actual source UI, synthetic authority, intercepted HTTP in fresh contexts.
// This never publishes merchant content or contacts the application API/database.
async page => {
    const checks = [], failures = [], pageErrors = [], contexts = [], requests = [];
    const check = (name, passed) => { checks.push({ name, passed: Boolean(passed) }); if (!passed) throw new Error(name); };
    const tenantId = '22222222-2222-4222-8222-222222222222';
    const pageId = '33333333-3333-4333-8333-333333333333';
    const recordId = '44444444-4444-4444-8444-444444444444';
    const theme = { presetKey: 'default', colors: { primary: '#2563eb', secondary: '#0f172a', background: '#ffffff', text: '#020617', muted: '#64748b' }, font: 'Inter', baseSize: '16px', borderRadius: 8 };
    let config = { tenantId, storeName: 'SYNTHETIC Phase2', storefrontStatus: 'live', storefrontTheme: 'modern', themeId: 'default', rowVersion: '10', currency: 'EGP', pages: { home: { sections: [] } }, livePages: { home: { sections: [] } }, productTemplates: {}, draftTheme: theme, liveTheme: theme };
    let cms = { id: pageId, tenantId, slug: 'about', title: 'About', contentType: 'RichText', status: 'Draft', draft: { recordId, slug: 'about', title: 'About', contentType: 'RichText', content: '<p>Original</p>', schemaVersion: 1, version: 1, seo: {} }, published: null };
    let nav = { rowVersion: '10', draft: { header: [{ label: 'Home', type: 'custom', path: '/', enabled: true, order: 0 }], footer: [] }, live: { header: [], footer: [] }, hasPendingChanges: true, validationWarnings: [], brokenLinks: [] };
    let footer = { scope: 'Tenant', revision: 'a'.repeat(64), updatedAtUtc: new Date().toISOString(), sections: [{ id: recordId, title: 'Company', order: 0, isActive: true, links: [] }], socialLinks: [] };
    const targets = { categories: [], products: [], cmsPages: [{ type: 'cms', targetId: pageId, label: 'About', state: 'published', resolvedPath: '/about' }], collections: [] };
    let configFails = false, heldCms = null, heldNav = null, heldFooter = null, rollbackRequest = null;
    let cmsWrites = 0, navPublishes = 0, footerWrites = 0, pageWrites = 0, themeWrites = 0;
    const adminContext = await page.context().browser().newContext({ viewport: { width: 1440, height: 1000 } }); contexts.push(adminContext);
    const tab = await adminContext.newPage(); tab.on('pageerror', e => pageErrors.push(e.message));
    const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    await adminContext.route('**/__phase2__', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><body><div id="fixture"></div></body></html>' }));
    await adminContext.route('**/api/**', async route => {
        const path = route.request().url().replace(/^https?:\/\/[^/]+/, '').split('?')[0];
        if (!path.startsWith('/api/')) return route.continue();
        const method = route.request().method(); requests.push({ path, method });
        const body = () => route.request().postDataJSON();
        if (path === '/api/admin/store-config') return json(route, configFails ? { message: 'Controlled outage' } : config, configFails ? 503 : 200);
        if (path === '/api/storefront/store-config') return json(route, config);
        if (path === '/api/admin/store-config/themes/presets') return json(route, []);
        if (path.startsWith('/api/admin/store-config/pages/') && method === 'PUT') {
            pageWrites++; const value = body();
            config = { ...config, rowVersion: String(Number(config.rowVersion) + 1), pages: { home: value } };
            return json(route, config);
        }
        if (path === `/api/admin/pages/${pageId}` && method === 'GET') return json(route, cms);
        if (path === `/api/admin/pages/${pageId}` && method === 'PUT') { cmsWrites++; heldCms = { route, body: body() }; return; }
        if (path === '/api/admin/cms/navigation/targets') return json(route, targets);
        if (path === '/api/admin/cms/navigation' && method === 'GET') return json(route, nav);
        if (path === '/api/admin/cms/navigation' && method === 'PUT') {
            const value = body(); nav = { ...nav, draft: { header: value.header, footer: value.footer }, rowVersion: '11' }; return json(route, nav);
        }
        if (path === '/api/admin/cms/navigation/publish') { navPublishes++; heldNav = route; return; }
        if (path === '/api/admin/footer' && method === 'GET') return json(route, footer);
        if (path === '/api/admin/footer' && method === 'PUT') { footerWrites++; heldFooter = { route, body: body() }; return; }
        if (path === '/api/admin/store-config/theme/overrides') {
            themeWrites++; const value = body();
            config = { ...config, rowVersion: '12', draftTheme: { ...theme, colors: value.colors, font: value.typography.fontFamily } }; return json(route, config);
        }
        if (path === '/api/admin/store-config/versions') return json(route, { versions: [{ version: 2, publishedAt: new Date().toISOString(), publishedBy: 'Fixture actor', isCurrent: true }, { version: 1, publishedAt: new Date().toISOString(), publishedBy: 'Fixture actor', isCurrent: false }] });
        if (path === '/api/admin/store-config/rollback/1') { rollbackRequest = { body: body(), ifMatch: route.request().headers()['if-match'] }; return json(route, { message: 'Controlled stale revision', code: 'CONCURRENCY_CONFLICT' }, 409); }
        // No unrecognized write is accepted or forwarded.
        return json(route, { message: 'Controlled dependency unavailable' }, 503);
    });
    try {
        await tab.goto('http://localhost:5173/__phase2__');
        await tab.evaluate(async ({ tenantId, pageId }) => {
            const refresh = await import('/@react-refresh'); refresh.injectIntoGlobalHook(window);
            window.$RefreshReg$ = () => {}; window.$RefreshSig$ = () => type => type;
            const React = (await import('/node_modules/.vite/deps/react.js')).default;
            const { createRoot } = (await import('/node_modules/.vite/deps/react-dom_client.js')).default;
            const source = await (await fetch('/src/modules/storefront/pages/ThemeEditorPage.jsx')).text();
            const dep = path => source.match(new RegExp('"(' + path.replaceAll('.', '\\.') + '[^" ]*)"'))[1];
            const { ToastProvider } = await import(dep('/src/components/Toast.jsx'));
            const { useAuthStore } = await import(dep('/src/store/authStore.js'));
            const routerSource = await (await fetch('/src/modules/storefront/cms/pages/CmsPageEditor.jsx')).text();
            const { MemoryRouter, Routes, Route } = await import(routerSource.match(/from "([^"]*react-router-dom[^"]*)"/)[1]);
            const i18n = (await import('/src/i18n/config.js')).default; await i18n.changeLanguage('en');
            useAuthStore.setState({ user: { id: '55555555-5555-4555-8555-555555555555', tenantId, role: 'Owner' }, isAuthenticated: true, hasHydrated: true });
            const { AuthoringPlatformProvider } = await import('/src/modules/storefront/authoringPlatform/index.js');
            const modules = {
                builder: (await import('/src/modules/storefront/pages/StorefrontBuilderPage.jsx')).StorefrontBuilderPage,
                cms: (await import('/src/modules/storefront/cms/pages/CmsPageEditor.jsx')).CmsPageEditor,
                nav: (await import('/src/modules/storefront/cms/pages/NavigationEditor.jsx')).NavigationEditor,
                footer: (await import('/src/modules/storefront/cms/pages/StorefrontFooterSettings.jsx')).StorefrontFooterSettings,
                theme: (await import('/src/modules/storefront/pages/ThemeEditorPage.jsx')).ThemeEditorPage,
                market: (await import('/src/modules/storefront/pages/ThemeMarketplacePage.jsx')).ThemeMarketplacePage,
                history: (await import('/src/modules/storefront/pages/VersionHistoryPage.jsx')).VersionHistoryPage,
            };
            const root = createRoot(document.getElementById('fixture')); let mountId = 0;
            window.phase2Mount = (kind, path) => root.render(React.createElement(MemoryRouter, { key: ++mountId, initialEntries: [path] }, React.createElement(ToastProvider, null, React.createElement(AuthoringPlatformProvider, null, React.createElement(Routes, null, React.createElement(Route, { path: kind === 'cms' ? '/storefront/content/pages/:pageId' : '*', element: React.createElement(modules[kind]) }))))));
            window.phase2Builder = (await import('/src/modules/storefront/store/index.js')).useBuilderStore;
            window.phase2Cms = (await import('/src/modules/storefront/cms/store/index.js')).useCmsStore;
            window.phase2Registry = (await import('/src/modules/storefront/builder/index.js')).sectionRegistry;
        }, { tenantId, pageId });
        await tab.evaluate(() => window.phase2Mount('builder', '/storefront/homepage'));
        await tab.getByRole('button', { name: 'Save Draft', exact: true }).waitFor();
        check('Homepage admits the synthetic backend workspace', (await tab.locator('body').innerText()).includes('Homepage'));
        await tab.evaluate(() => window.phase2Builder.getState().addSection('text-block', undefined, window.phase2Registry));
        await tab.getByRole('button', { name: 'Save Draft', exact: true }).click();
        await tab.waitForFunction(() => !window.phase2Builder.getState().isDirty);
        check('Homepage Save Draft serializes the actual candidate and accepts readback', pageWrites === 1 && config.pages.home.sections.length === 1);

        await tab.evaluate(() => window.phase2Mount('builder', '/storefront/catalog/product-template?templateId=default'));
        await tab.getByRole('button', { name: 'New Template' }).waitFor();
        check('Catalog route is the actual Product Template editor; built-in default cannot be deleted', await tab.getByRole('button', { name: 'Delete', exact: true }).first().isDisabled());

        await tab.evaluate(pageId => window.phase2Mount('cms', `/storefront/content/pages/${pageId}`), pageId);
        const editor = tab.getByRole('textbox', { name: 'Content', exact: true }); await editor.waitFor();
        await editor.fill('Typed continuously مرحباً');
        await editor.press('Control+s');
        await tab.waitForFunction(() => document.body.innerText.includes('Saving'));
        await editor.press('Control+s');
        check('CMS keyboard save includes rich text before blur and suppresses duplicate submission', cmsWrites === 1 && heldCms.body.content.includes('مرحباً'));
        await editor.fill('Newer local text');
        cms = { ...cms, draft: { ...cms.draft, ...heldCms.body, version: 2 } };
        await json(heldCms.route, cms);
        await tab.getByText('Draft saved. Newer unsaved edits were retained.').waitFor();
        check('CMS preserves newer focused input when the earlier save completes', (await editor.innerText()).includes('Newer local text'));

        await tab.evaluate(() => window.phase2Mount('nav', '/storefront/navigation?view=header'));
        const publish = tab.getByRole('button', { name: 'Publish Header and Footer Navigation', exact: true }); await publish.waitFor(); await publish.click();
        await tab.getByRole('dialog').getByRole('button', { name: 'Publish Header and Footer Navigation' }).click();
        await tab.waitForFunction(() => document.body.innerText.includes('Publishing'));
        await tab.evaluate(() => window.phase2Cms.getState().updateNavigationHeader([{ label: 'Newer navigation', type: 'custom', path: '/', enabled: true, order: 0 }]));
        nav = { ...nav, live: nav.draft, rowVersion: '12', hasPendingChanges: false };
        await json(heldNav, nav);
        await tab.getByText('Newer navigation', { exact: true }).waitFor();
        check('Navigation publication retains newer candidate and rotates accepted revision', navPublishes === 1 && await tab.evaluate(() => window.phase2Cms.getState().isNavigationDirty && window.phase2Cms.getState().navigation.rowVersion === '12'));

        await tab.evaluate(() => window.phase2Mount('footer', '/storefront/footer-content'));
        const title = tab.getByPlaceholder('Section title'); await title.waitFor(); await title.fill('Submitted footer');
        await tab.getByRole('button', { name: 'Save live Footer', exact: true }).click();
        await tab.getByRole('dialog').getByRole('button', { name: 'Save live Footer' }).click();
        await tab.waitForFunction(() => document.body.innerText.includes('Saving'));
        await title.evaluate(el => { const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set; setter.call(el, 'Newer footer'); el.dispatchEvent(new Event('input', { bubbles: true })); });
        footer = { ...footer, ...heldFooter.body, revision: 'b'.repeat(64) };
        await json(heldFooter.route, footer);
        await tab.getByText('The submitted Footer is live. Newer unsaved edits were retained.').waitFor();
        check('Footer live save preserves newer local edits and truthfully names its live scope', footerWrites === 1 && await title.inputValue() === 'Newer footer');

        await tab.evaluate(() => window.phase2Mount('theme', '/storefront/appearance/colors-typography'));
        const primary = tab.getByLabel('Primary Color', { exact: true }); await primary.waitFor(); await primary.fill('invalid');
        check('Theme invalid token prevents Save Draft', await tab.getByRole('button', { name: 'Save Draft', exact: true }).isDisabled());
        await primary.fill('#123456'); await tab.getByRole('button', { name: 'Save Draft', exact: true }).click();
        await tab.getByText('Appearance draft saved', { exact: true }).waitFor();
        check('Theme saves only draft overrides through the actual client', themeWrites === 1 && config.liveTheme.colors.primary !== config.draftTheme.colors.primary);

        configFails = true;
        await tab.evaluate(() => window.phase2Mount('market', '/storefront/appearance/themes'));
        await tab.getByText('Themes unavailable', { exact: true }).waitFor();
        check('Theme catalog failure is not represented as no active Theme', await tab.getByText('No active Theme', { exact: true }).count() === 0);
        configFails = false; await tab.getByRole('button', { name: 'Retry', exact: true }).click();
        await tab.getByRole('heading', { name: 'Active Theme', exact: true }).waitFor();
        check('Theme catalog Retry recovers active identity', true);

        await tab.evaluate(() => window.phase2Mount('history', '/storefront/storeconfig-version-history'));
        await tab.getByRole('button', { name: 'Rollback', exact: true }).click();
        const dialog = tab.getByRole('dialog', { name: 'Confirm Rollback' });
        check('History explains CMS/Footer/draft exclusions and requires confirmation', (await dialog.innerText()).includes('Current drafts, CMS Pages, and separate Footer Content are preserved') && await dialog.getByRole('button', { name: 'Rollback Now' }).isDisabled());
        const reviewedVersion = config.rowVersion; config = { ...config, rowVersion: '99' };
        await dialog.getByRole('checkbox').check(); await dialog.getByRole('button', { name: 'Rollback Now' }).click();
        await tab.getByText('Rollback failed', { exact: true }).waitFor();
        check('History uses the reviewed revision, rejects conflict and keeps confirmation for explicit retry', rollbackRequest.ifMatch === `"${reviewedVersion}"` && await dialog.isVisible());
        await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
    } catch (error) { failures.push({ group: 'admin-controlled', error: error.message.split('\n')[0], body: (await tab.locator('body').innerText()).slice(0, 5000) }); }
    finally { for (const context of contexts) await context.close(); }
    return { evidence: 'controlled-browser-source-with-intercepted-http', checks, failures, pageErrors, requests, realApplicationWrites: 0 };
}

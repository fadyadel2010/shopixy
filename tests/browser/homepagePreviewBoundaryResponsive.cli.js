// Read-only signed Theme candidates: Live A stays unchanged while preview uses B.
async page => {
    page.setDefaultTimeout(30000);
    const handleDialog = async d => { if (d.type() === 'beforeunload') await d.accept(); };
    page.on('dialog', handleDialog);
    try {
        const themes = await page.evaluate(async () => (await import('/src/modules/storefront/themes/themeMarketplaceRegistry.js')).getAvailableMarketplaceThemes().map(t => ({ id: t.id, name: t.name })));
        const live = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
        const baseline = await page.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/store-config')).data.storefrontTheme);
        const results = [];
        for (const theme of themes) {
            const access = await page.evaluate(async themeId => (await (await import('/src/modules/storefront/services/storeConfigService.js')).storeConfigService.createThemePreviewLink(themeId)), theme.id);
            for (const language of ['en', 'ar']) {
                await page.setViewportSize({ width: 390, height: 844 });
                await page.goto(`http://localhost:5173/storefront/homepage?themePreviewToken=${encodeURIComponent(access.previewToken)}`);
                await page.getByRole('button', { name: /^(Preview Saved Draft|معاينة المسودة المحفوظة)$/ }).waitFor();
                if ((await page.locator('html').getAttribute('lang')) !== language) await page.getByRole('button', { name: language === 'ar' ? 'AR' : 'EN', exact: true }).click();
                await page.getByRole('button', { name: /^(Preview Saved Draft|معاينة المسودة المحفوظة)$/ }).click();
                await page.getByRole('button', { name: language === 'ar' ? 'جوال' : 'Mobile', exact: true }).click();
                const frame = page.frameLocator('iframe'); await frame.locator('header').waitFor(); await frame.locator('footer').waitFor();
                await page.waitForTimeout(500);
                const runtime = page.frames().find(f => f.url().includes('/draft-preview.html'));
                const state = await runtime.evaluate(async () => ({ theme: (await import('/src/store/storeConfigStore.ts')).useStoreConfigStore.getState().config.storefrontTheme,
                    width: innerWidth, height: innerHeight, direction: document.documentElement.dir,
                    overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1 }));
                if (state.theme !== theme.id || state.width !== 390 || state.height !== 844 || state.overflow) throw new Error(`Signed candidate mobile failure: ${JSON.stringify(state)}`);
                const menu = frame.getByRole('button', { name: /menu|القائمة/i }).first();
                await page.bringToFront();
                await page.waitForTimeout(250);
                // Playwright's locator hit-testing does not account for a scaled
                // cross-origin iframe. Click its real headed-screen coordinate.
                const button = await menu.evaluate(e => e.getBoundingClientRect().toJSON());
                const box = await page.locator('iframe').boundingBox();
                const scale = box.width / state.width;
                await runtime.evaluate(() => document.addEventListener('click', e => { window.__previewMenuClick = { tag: e.target.tagName, label: e.target.closest('button')?.getAttribute('aria-label') }; }, { capture: true, once: true }));
                await page.mouse.click(Math.round(box.x + (button.x + button.width / 2) * scale),
                    Math.round(box.y + (button.y + button.height / 2) * scale));
                await runtime.waitForTimeout(300);
                const visibleNavigation = await frame.getByRole('link', { name: /Home|HOME/ }).count();
                if (!visibleNavigation) throw new Error(`Theme mobile menu navigation is missing ${JSON.stringify({ theme: theme.id, language, state, box, button, scale, click: await runtime.evaluate(() => window.__previewMenuClick), text: (await frame.locator('header').innerText()).slice(0,300) })}`);
                await page.screenshot({ path: `.artifacts/gap-signed-candidate-${theme.id}-${language}-admin-mobile-menu.png` });
                const adminOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1);
                if (adminOverflow) throw new Error('Admin preview framing overflows mobile');
                results.push({ candidate: theme.id, liveTheme: baseline, language, state, adminOverflow, visibleNavigation });
            }
        }
        await live.reload();
        await live.locator('header').waitFor();
        await live.waitForTimeout(500);
        await live.waitForFunction(async expected => (await import('/src/store/storeConfigStore.ts')).useStoreConfigStore.getState().config.storefrontTheme === expected, baseline);
        const currentLive = await live.evaluate(async () => (await import('/src/store/storeConfigStore.ts')).useStoreConfigStore.getState().config.storefrontTheme);
        if (currentLive !== baseline) throw new Error(`Read-only Theme candidates mutated live selection: expected ${baseline}, rendered ${currentLive}`);
        await page.evaluate(results => sessionStorage.setItem('gap-signed-responsive-results', JSON.stringify(results)), results);
        return { themes: themes.length, cells: results.length, liveUnchanged: currentLive, overflow: false, mobileMenus: true };
    } finally { page.off('dialog', handleDialog); }
}

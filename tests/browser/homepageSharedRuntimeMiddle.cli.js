// Final read-only card/section parity sweep, using existing signed theme candidates.
async page => {
    page.setDefaultTimeout(20000);
    await page.addInitScript(() => window.addEventListener('beforeunload', event => event.stopImmediatePropagation(), true));
    const themes = await page.evaluate(async () => (await import('/src/modules/storefront/themes/themeMarketplaceRegistry.js')).getAvailableMarketplaceThemes().map(t => t.id));
    const index = await page.evaluate(() => Number(sessionStorage.getItem('gap-middle-index') || '0'));
    if (index >= themes.length) return { done: true };
    const context = await page.context().browser().newContext();
    const reference = await context.newPage();
    const results = [];
    const prepare = async (runtime, theme, language) => {
        const owner = typeof runtime.page === 'function' ? runtime.page() : runtime;
        await owner.bringToFront();
        await runtime.waitForFunction(async ({ theme, language }) => {
            const config = (await import('/src/store/storeConfigStore.ts')).useStoreConfigStore.getState().config;
            const locale = (await import('/src/i18n/languageStore.ts')).useLanguageStore.getState();
            if (locale.language !== language) locale.setLanguage(language);
            return config.storefrontTheme === theme && document.documentElement.lang === language;
        }, { theme, language });
        await runtime.locator('article').first().waitFor();
        await runtime.locator('article img').evaluateAll(images => Promise.all(images.map(i => { i.loading = 'eager'; return i.decode().catch(() => null); })));
        await runtime.evaluate(() => document.fonts.ready);
        await runtime.locator('[data-section-type="featured-products"]').scrollIntoViewIfNeeded();
        await runtime.waitForTimeout(400);
        // Align the same section immediately below the sticky Header. Generic
        // scrollIntoView can choose different visible portions of a tall carousel.
        for (let pass = 0; pass < 2; pass++) {
            await runtime.evaluate(() => {
                const section = document.querySelector('[data-section-type="featured-products"]');
                const header = document.querySelector('header').getBoundingClientRect();
                window.scrollTo({ top: window.scrollY + section.getBoundingClientRect().top - header.bottom - 24, behavior: 'instant' });
            });
            await runtime.waitForTimeout(150);
        }
        return runtime.evaluate(() => ({
            overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
            cards: document.querySelectorAll('article').length,
            sectionY: document.querySelector('[data-section-type="featured-products"]').getBoundingClientRect().top,
            headerBottom: document.querySelector('header').getBoundingClientRect().bottom,
            sections: [...document.querySelectorAll('[data-section-id]')].map(e => ({ id: e.getAttribute('data-section-id'), type: e.getAttribute('data-section-type') })),
        }));
    };
    try {
        for (const theme of themes.slice(index, index + 1)) {
            const access = await page.evaluate(async id => (await (await import('/src/modules/storefront/services/storeConfigService.js')).storeConfigService.createThemePreviewLink(id)), theme);
            for (const language of ['en', 'ar']) {
                await page.setViewportSize({ width: 1600, height: 1000 });
                await page.goto(`http://localhost:5173/storefront/homepage?themePreviewToken=${encodeURIComponent(access.previewToken)}`);
                await page.getByRole('button', { name: /^(Preview Saved Draft|معاينة المسودة المحفوظة)$/ }).waitFor();
                if ((await page.locator('html').getAttribute('lang')) !== language) await page.getByRole('button', { name: language === 'ar' ? 'AR' : 'EN', exact: true }).click();
                await page.getByRole('button', { name: /^(Preview Saved Draft|معاينة المسودة المحفوظة)$/ }).click();
                await page.frameLocator('iframe').locator('header').waitFor();
                const runtime = page.frames().find(f => f.url().includes('/draft-preview.html'));
                for (const device of ['desktop', 'mobile']) {
                    await page.getByRole('button', { name: language === 'ar' ? (device === 'desktop' ? 'سطح المكتب' : 'جوال') : (device === 'desktop' ? 'Desktop' : 'Mobile'), exact: true }).click();
                    await page.waitForTimeout(350);
                    const preview = await prepare(runtime, theme, language);
                    const viewport = await runtime.evaluate(() => ({ width: innerWidth, height: innerHeight }));
                    await page.locator('iframe').screenshot({ path: `.artifacts/gap-middle-${theme}-${language}-${device}-preview.png`, animations: 'disabled' });
                    await reference.setViewportSize(viewport);
                    await reference.goto(`${access.previewUrl}&lang=${language}`);
                    await reference.locator('header').waitFor();
                    const live = await prepare(reference, theme, language);
                    await reference.screenshot({ path: `.artifacts/gap-middle-${theme}-${language}-${device}-storefront.png`, animations: 'disabled' });
                    if (preview.overflow || live.overflow || preview.cards !== live.cards || JSON.stringify(preview.sections) !== JSON.stringify(live.sections)) throw new Error(`Section/card parity failed ${theme}/${language}/${device}`);
                    results.push({ theme, language, device, viewport, preview, live });
                }
            }
        }
        await page.evaluate(({ results, index }) => {
            const previous = JSON.parse(sessionStorage.getItem('gap-middle-results') || '[]');
            sessionStorage.setItem('gap-middle-results', JSON.stringify([...previous, ...results]));
            sessionStorage.setItem('gap-middle-index', String(index + 1));
        }, { results, index });
        await page.goto('http://localhost:5173/storefront/homepage');
        return { theme: themes[index], cells: results.length, headedSectionCardScreenshots: results.length * 2, overflow: false, publicationMutations: 0 };
    } finally { await context.close(); }
}

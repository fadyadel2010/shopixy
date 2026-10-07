// Headed opencode-ui-test. One supported theme per invocation, real authoring controls.
async page => {
    page.setDefaultTimeout(30000);
    // All draft saves are awaited below. Suppress only the browser's leave-page
    // prompt in this test page so CLI modal bookkeeping cannot detach a running
    // screenshot comparison. Application publication/auth/data APIs stay real.
    await page.addInitScript(() => window.addEventListener('beforeunload', event => event.stopImmediatePropagation(), true));
    const handleDialog = async d => { if (d.type() === 'beforeunload') await d.accept(); };
    page.on('dialog', handleDialog);
    const read = async path => page.evaluate(async path => (await (await import('/src/services/api.js')).default.get(path)).data, path);
    const cleanHome = () => page.waitForFunction(async () => !(await import('/src/modules/storefront/store/index.js')).useBuilderStore.getState().isDirty);
    const cleanHeader = () => page.waitForFunction(async () => !(await import('/src/modules/storefront/cms/store/index.js')).useCmsStore.getState().isNavigationDirty);
    const response = (suffix, method) => page.waitForResponse(r => r.url().endsWith(suffix) && r.request().method() === method);
    const accept = async promise => { const r = await promise; if (r.status() !== 200) throw new Error(`${r.status()} ${await r.text()}`); return r.json(); };
    const themes = await page.evaluate(async () => (await import('/src/modules/storefront/themes/themeMarketplaceRegistry.js')).getAvailableMarketplaceThemes().map(t => ({ id: t.id, name: t.name })));
    const index = await page.evaluate(() => Number(sessionStorage.getItem('gap-preview-index') || '0'));
    const theme = themes[index];
    if (!theme) return { done: true, count: themes.length };
    const live = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/')) || await page.context().newPage();
    let referenceContext = null;
    const title = `CERT Draft ${theme.name}`;
    const announcement = `CERT Draft Header ${theme.name}`;
    const announcementAr = `إعلان المسودة ${theme.name}`;
    const evidence = { theme: theme.id, cells: [], checkpoints: [] };
    await page.evaluate(() => sessionStorage.removeItem('gap-preview-error'));
    const go = async url => { await page.waitForTimeout(400); await page.goto(url); };
    const setAdminLanguage = async language => {
        if ((await page.locator('html').getAttribute('lang')) !== language) await page.getByRole('button', { name: language === 'ar' ? 'AR' : 'EN', exact: true }).click();
    };
    const openPreview = async () => {
        await page.getByRole('button', { name: /^(Preview Saved Draft|معاينة المسودة المحفوظة)$/ }).click();
        const frame = page.frames().find(f => f.url().includes('/draft-preview.html'));
        if (!frame) await page.waitForFunction(() => [...document.querySelectorAll('iframe')].length === 1);
        const loaded = () => page.frames().find(f => f.url().includes('/draft-preview.html'));
        for (let i = 0; i < 40 && !loaded(); i++) await page.waitForTimeout(100);
        const runtime = loaded();
        if (!runtime) throw new Error('Actual Storefront preview frame did not load');
        await runtime.locator('header').waitFor(); await runtime.locator('footer').waitFor();
        return runtime;
    };
    const closePreview = () => page.getByRole('button', { name: /^(Close Preview|إغلاق)$/ }).click();
    const saveHome = async () => { const saved = response('/admin/store-config/pages/home', 'PUT'); await page.getByRole('button', { name: 'Save Draft', exact: true }).click(); await accept(saved); await cleanHome(); };
    const saveHeader = async () => {
        const save = page.getByRole('button', { name: 'Save Draft', exact: true });
        if (await save.isEnabled()) await save.click();
        await cleanHeader();
    };
    const editHeader = async () => {
        await go('http://localhost:5173/storefront/navigation?view=header');
        await page.getByLabel('Announcement text (English)', { exact: true }).fill(announcement);
        await page.getByLabel('Announcement text (Arabic)', { exact: true }).fill(announcementAr);
        await page.getByLabel('Show announcement bar', { exact: true }).check();
        await saveHeader();
    };
    const ready = async (runtime, language, expectedTitle) => {
        const owner = typeof runtime.page === 'function' ? runtime.page() : runtime;
        await owner.bringToFront();
        await runtime.getByRole('heading', { name: expectedTitle, exact: false }).waitFor();
        // Exercise the real lazy-load viewport before raster capture. An off-screen
        // iframe can legitimately leave catalog images deferred by Chromium.
        await runtime.locator('article').first().scrollIntoViewIfNeeded();
        await runtime.locator('article img').evaluateAll(images => Promise.all(images.map(image => {
            image.loading = 'eager';
            return image.decode().catch(() => null);
        })));
        await runtime.evaluate(() => window.scrollTo(0, 0));
        await runtime.waitForFunction(async () => { await document.fonts.ready; return [...document.querySelectorAll('img')].every(i => !i.getAttribute('src') || i.complete); });
        await runtime.waitForTimeout(650);
        const metrics = await runtime.evaluate(() => {
            const rect = e => { const r = e.getBoundingClientRect(); const css = getComputedStyle(e); return { width: r.width, height: r.height, font: css.fontFamily, fontSize: css.fontSize }; };
            return { width: innerWidth, height: innerHeight, lang: document.documentElement.lang, dir: document.documentElement.dir,
                contentLeft: document.body.getBoundingClientRect().left, contentWidth: document.documentElement.clientWidth,
                overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
                header: rect(document.querySelector('header')), footer: rect(document.querySelector('footer')),
                sectionOrder: [...document.querySelectorAll('[data-section-id]')].map(e => e.getAttribute('data-section-id')) };
        });
        if (metrics.lang !== language || metrics.dir !== (language === 'ar' ? 'rtl' : 'ltr') || metrics.overflow) throw new Error(`Locale/overflow failure ${JSON.stringify(metrics)}`);
        return metrics;
    };
    const screenshotFrame = async (runtime, suffix) => {
        await runtime.evaluate(() => window.scrollTo(0, 0));
        const top = await page.locator('iframe').screenshot({ path: `.artifacts/gap-parity-${theme.id}-${suffix}-preview-top.png`, animations: 'disabled' });
        await runtime.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
        const footer = await page.locator('iframe').screenshot({ path: `.artifacts/gap-parity-${theme.id}-${suffix}-preview-footer.png`, animations: 'disabled' });
        return { top, footer };
    };
    const compare = async (left, right, contentOffset) => page.evaluate(async ({ a, b, contentOffset }) => {
        const image = async data => { const i = new Image(); i.src = `data:image/png;base64,${data}`; await i.decode(); const c = document.createElement('canvas'); c.width = i.width; c.height = i.height; const ctx = c.getContext('2d'); ctx.drawImage(i, 0, 0); return { width: c.width, height: c.height, data: ctx.getImageData(0, 0, c.width, c.height).data }; };
        const x = await image(a), y = await image(b);
        if (Math.abs(x.width - y.width) > 2 || Math.abs(x.height - y.height) > 2) return { dimensionsMatch: false, a: [x.width, x.height], b: [y.width, y.height] };
        let best = { ratio: 1, changedPixels: 0, comparedPixels: 0, dx: 0, dy: 0 };
        const w = Math.min(x.width, y.width), h = Math.min(x.height, y.height);
        for (let dy = -1; dy <= 1; dy++) for (let dx = contentOffset - 1; dx <= contentOffset + 1; dx++) {
            let changed = 0, total = 0;
            for (let row = 20; row < h - 20; row++) for (let col = 20; col < w - 20; col++) {
                const p = ((row + dy) * x.width + col + dx) * 4, q = (row * y.width + col) * 4;
                if (Math.abs(x.data[p] - y.data[q]) > 24 || Math.abs(x.data[p+1] - y.data[q+1]) > 24 || Math.abs(x.data[p+2] - y.data[q+2]) > 24) changed++;
                total++;
            }
            if (changed / total < best.ratio) best = { ratio: changed / total, changedPixels: changed, comparedPixels: total, dx, dy };
        }
        return { dimensionsMatch: x.width === y.width && x.height === y.height, framingWithinTwoPixels: true, a: [x.width, x.height], b: [y.width, y.height], ...best, edgeExclusion: 20, rgbThreshold: 24 };
    }, { a: left.toString('base64'), b: right.toString('base64'), contentOffset });
    try {
        await page.setViewportSize({ width: 1600, height: 1000 });
        await go('http://localhost:5173/storefront/appearance/themes');
        await setAdminLanguage('en');
        const current = (await read('/admin/store-config')).storefrontTheme;
        if (current !== theme.id) {
            const card = page.getByRole('heading', { name: theme.name, exact: true }).locator('xpath=../../..');
            await card.getByRole('button', { name: 'Activate theme now', exact: true }).click();
            const activation = response('/admin/store-config/theme/activate', 'POST');
            await page.getByRole('dialog').getByRole('button', { name: 'Activate theme now', exact: true }).click();
            await accept(activation); await page.getByRole('dialog').waitFor({ state: 'hidden' });
        }
        await go('http://localhost:5173/storefront/homepage');
        const baseline = await read('/admin/store-config');
        const baselineHeader = (await read('/admin/cms/navigation')).live.announcement;
        await page.getByRole('button', { name: 'Edit', exact: true }).first().click();
        const uploaded = response('/admin/homepage-media', 'POST');
        await page.getByLabel('Background Image', { exact: true }).setInputFiles('.artifacts/media-cert-primary.png');
        const asset = await accept(uploaded); evidence.asset = asset.reference;
        await page.getByRole('img', { name: 'Image preview', exact: true }).evaluate(i => i.decode());
        await page.getByRole('main').getByRole('textbox').first().fill(title); await saveHome();
        await editHeader();
        await go('http://localhost:5173/storefront/homepage'); await page.waitForTimeout(400); await page.reload();
        let runtime = await openPreview();
        await runtime.getByRole('heading', { name: title, exact: false }).waitFor();
        // Reload only the embedded runtime: the parent must re-send the admitted DTO.
        await runtime.evaluate(() => location.reload());
        await runtime.getByRole('heading', { name: title, exact: false }).waitFor();
        await live.goto('http://ahramstore.localhost:5174/');
        const publicBoundary = await live.evaluate(async reference => {
            const config = await (await fetch('/api/storefront/store-config', { cache: 'no-store' })).json();
            const nav = await (await fetch('/api/storefront/navigation', { cache: 'no-store' })).json();
            return { imageStatus: (await fetch(reference, { cache: 'no-store' })).status, title: config.pages.home.sections[0].settings.title, announcement: nav.header.announcement?.text,
                previewStatus: (await fetch('/api/storefront/config/preview', { cache: 'no-store' })).status, publicHasPreviewChrome: !!config.previewChrome };
        }, asset.reference);
        if (publicBoundary.imageStatus !== 404 || publicBoundary.title === title || publicBoundary.announcement === announcement || publicBoundary.previewStatus !== 401 || publicBoundary.publicHasPreviewChrome) throw new Error(`Draft leaked ${JSON.stringify(publicBoundary)}`);
        evidence.publicBoundary = publicBoundary;
        const captured = [];
        for (const language of ['en', 'ar']) {
            if ((await page.locator('html').getAttribute('lang')) !== language) {
                await closePreview(); await setAdminLanguage(language); runtime = await openPreview();
            }
            for (const device of ['desktop', 'mobile']) {
                await page.getByRole('button', { name: language === 'ar' ? (device === 'desktop' ? 'سطح المكتب' : 'جوال') : (device === 'desktop' ? 'Desktop' : 'Mobile'), exact: true }).click();
                await page.waitForTimeout(400);
                const metrics = await ready(runtime, language, title);
                if (device === 'mobile' && (metrics.width !== 390 || metrics.height !== 844)) throw new Error(`Mobile preview is not 390x844: ${JSON.stringify(metrics)}`);
                const region = runtime.getByRole('region', { name: language === 'ar' ? 'إعلان المتجر' : 'Store announcement', exact: true });
                if (!(await region.innerText()).toLocaleLowerCase().includes((language === 'ar' ? announcementAr : announcement).toLocaleLowerCase())) throw new Error('Draft Header announcement missing');
                const media = await runtime.evaluate(async () => {
                    const state = (await import('/src/store/storeConfigStore.ts')).useStoreConfigStore.getState().config;
                    const source = state.pages.home.sections[0].settings.backgroundImage;
                    const img = new Image(); img.src = source; await img.decode();
                    return { decoded: true, width: img.naturalWidth, rendered: [...document.querySelectorAll('main img')].some(e => e.getAttribute('src') === source) || [...document.querySelectorAll('main *')].some(e => getComputedStyle(e).backgroundImage.includes(source)) };
                });
                if (!media.rendered) throw new Error('Draft upload is not actually rendered');
                const suffix = `${language}-${device}`;
                captured.push({ language, device, metrics, media, images: await screenshotFrame(runtime, suffix) });
            }
        }
        await closePreview(); await setAdminLanguage('en');
        // Saved-draft discard across both existing publication scopes.
        await page.getByRole('button', { name: 'Discard', exact: true }).click(); await cleanHome();
        await go('http://localhost:5173/storefront/navigation?view=header');
        await page.getByRole('button', { name: 'Discard', exact: true }).click(); await cleanHeader();
        await go('http://localhost:5173/storefront/homepage'); runtime = await openPreview();
        await runtime.getByRole('heading', { name: baseline.livePages.home.sections[0].settings.title, exact: false }).waitFor();
        const discarded = await runtime.evaluate(async () => ({ config: (await import('/src/store/storeConfigStore.ts')).useStoreConfigStore.getState().config,
            nav: (await import('/src/stores/navigationStore.ts')).useNavigationStore.getState().navigation }));
        if (discarded.config.pages.home.sections[0].settings.backgroundImage !== baseline.livePages.home.sections[0].settings.backgroundImage || discarded.nav.header.announcement?.text !== baselineHeader?.text) throw new Error('Discard did not return preview to live');
        await closePreview();
        await page.getByRole('button', { name: 'Edit', exact: true }).first().click();
        await page.getByRole('main').getByRole('textbox').first().fill(title);
        await page.locator('input[type=url]').fill(asset.reference); await saveHome();
        await editHeader();
        await go('http://localhost:5173/storefront/homepage');
        await page.getByRole('button', { name: 'Publish Homepage', exact: true }).click();
        const homePublished = response('/admin/store-config/pages/home/publish', 'POST');
        await page.getByRole('dialog').getByRole('button', { name: 'Publish Homepage', exact: true }).click(); await accept(homePublished); await page.getByRole('dialog').waitFor({ state: 'hidden' });
        await go('http://localhost:5173/storefront/navigation?view=header');
        await page.getByRole('button', { name: 'Publish Header and Footer Navigation', exact: true }).click();
        const headerPublished = response('/admin/cms/navigation/publish', 'POST');
        await page.getByRole('dialog').getByRole('button', { name: 'Publish Header and Footer Navigation', exact: true }).click(); await accept(headerPublished); await page.getByRole('dialog').waitFor({ state: 'hidden' });
        referenceContext = await page.context().browser().newContext();
        const reference = await referenceContext.newPage(); // Guest reference, matching Admin preview's shopper identity.
        for (const cell of captured) {
            await reference.setViewportSize({ width: cell.metrics.width, height: cell.metrics.height });
            await reference.goto(`http://ahramstore.localhost:5174/?lang=${cell.language}`);
            await reference.waitForFunction(async language => { const s = (await import('/src/i18n/languageStore.ts')).useLanguageStore.getState(); s.setLanguage(language); return document.documentElement.lang === language; }, cell.language);
            const metrics = await ready(reference, cell.language, title);
            await reference.evaluate(() => window.scrollTo(0, 0));
            const top = await reference.screenshot({ path: `.artifacts/gap-parity-${theme.id}-${cell.language}-${cell.device}-live-top.png`, animations: 'disabled' });
            await reference.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
            const footer = await reference.screenshot({ path: `.artifacts/gap-parity-${theme.id}-${cell.language}-${cell.device}-live-footer.png`, animations: 'disabled' });
            const offset = Math.round(cell.metrics.contentLeft - metrics.contentLeft);
            const topPixels = await compare(cell.images.top, top, offset), footerPixels = await compare(cell.images.footer, footer, offset);
            evidence.cells.push({ language: cell.language, device: cell.device, preview: cell.metrics, live: metrics, media: cell.media, topPixels, footerPixels });
        }
        evidence.checkpoints.push('real device upload', 'authenticated complete shell', 'draft reload and frame reload', 'public draft/media/preview denial', 'saved Homepage and Header discard', 'controlled publish of same configuration', 'headed top/footer screenshots and raster comparison');
        await page.evaluate(({ evidence, index }) => {
            const results = JSON.parse(sessionStorage.getItem('gap-preview-results') || '[]'); results.push(evidence);
            sessionStorage.setItem('gap-preview-results', JSON.stringify(results)); sessionStorage.setItem('gap-preview-index', String(index + 1));
        }, { evidence, index });
        return { theme: theme.id, cells: evidence.cells.length,
            maxTopDifference: Math.max(...evidence.cells.map(c => c.topPixels.ratio)),
            maxFooterDifference: Math.max(...evidence.cells.map(c => c.footerPixels.ratio)),
            boundary: publicBoundary, discarded: true };
    } catch (error) {
        await page.evaluate(({ evidence, error }) => sessionStorage.setItem('gap-preview-error', JSON.stringify({ evidence, error })), { evidence, error: String(error) });
        await page.screenshot({ path: `.artifacts/gap-parity-${theme.id}-failure.png` }); throw error;
    } finally { page.off('dialog', handleDialog); if (referenceContext) await referenceContext.close(); }
}

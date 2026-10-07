// Run with playwright-cli -s=opencode-ui-test run-code --filename=tests/browser/homepageThemeAuthoring.cli.js
// One supported theme per invocation. Uses real controls in the existing headed session.
async page => {
    page.setDefaultTimeout(15000);
    const handleDialog = async dialog => { if (dialog.type() === 'beforeunload') await dialog.accept(); };
    page.on('dialog', handleDialog);
    const themes = await page.evaluate(async () => (await import('/src/modules/storefront/themes/themeMarketplaceRegistry.js')).getAvailableMarketplaceThemes().map(t => ({ id: t.id, name: t.name })));
    const index = await page.evaluate(() => Number(sessionStorage.getItem('homepage-theme-cert-index') || '0'));
    const theme = themes[index];
    if (!theme) return { done: true, themes: themes.length };
    const runId = String(Date.now()).slice(-6);
    const englishText = `CERT Collection ${theme.name} ${runId}`;
    const arabicText = `مجموعة اعتماد ${theme.name} ${runId}`;
    const live = page.context().pages().find(p => p.url().includes('ahramstore.localhost')) || await page.context().newPage();
    const evidence = { theme: theme.id, cells: [], checkpoints: [] };
    await page.evaluate(() => sessionStorage.removeItem('homepage-theme-cert-error'));
    const go = async url => { await page.waitForTimeout(300); await page.goto(url); };
    const reload = async () => { await page.waitForTimeout(300); await page.reload(); };
    const responseFor = (suffix, method) => page.waitForResponse(r => r.url().endsWith(suffix) && r.request().method() === method);
    const assertResponse = async promise => {
        const response = await promise;
        if (response.status() !== 200) throw new Error(`${response.status()}: ${JSON.stringify(await response.json())}`);
        return response.json();
    };
    const cleanHeader = () => page.waitForFunction(async () => !(await import('/src/modules/storefront/cms/store/index.js')).useCmsStore.getState().isNavigationDirty);
    const setAdminLanguage = async language => {
        if ((await page.locator('html').getAttribute('lang'))?.startsWith(language)) return;
        await page.getByRole('button', { name: language === 'ar' ? 'AR' : 'EN', exact: true }).click();
    };
    const setLiveLanguage = async language => {
        if ((await live.locator('html').getAttribute('lang'))?.startsWith(language)) return;
        await live.getByRole('button', { name: language === 'ar' ? 'Switch to Arabic' : 'Switch to English', exact: true }).click();
    };
    try {
        await page.bringToFront();
        await page.setViewportSize({ width: 1600, height: 1000 });
        await go('http://localhost:5173/storefront/appearance/themes');
        await page.getByRole('heading', { name: /^(Themes|الثيمات)$/, exact: true }).waitFor();
        await setAdminLanguage('en');
        const current = await page.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/store-config')).data.storefrontTheme);
        if (current !== theme.id) {
            const card = page.getByRole('heading', { name: theme.name, exact: true }).locator('xpath=../../..');
            await card.getByRole('button', { name: 'Activate theme now', exact: true }).click();
            const activated = responseFor('/admin/store-config/theme/activate', 'POST');
            await page.getByRole('dialog').getByRole('button', { name: 'Activate theme now', exact: true }).click();
            await assertResponse(activated);
            await page.getByRole('dialog').waitFor({ state: 'hidden' });
        }
        evidence.checkpoints.push('theme activated through Admin');
        await go('http://localhost:5173/storefront/homepage');
        await page.getByRole('button', { name: 'Edit', exact: true }).first().click();
        const uploaded = responseFor('/admin/homepage-media', 'POST');
        await page.getByLabel('Background Image', { exact: true }).setInputFiles('.artifacts/media-cert-primary.png');
        const asset = await assertResponse(uploaded);
        evidence.mediaReference = asset.reference;
        await page.getByRole('img', { name: 'Image preview', exact: true }).evaluate(i => i.decode());
        const saved = responseFor('/admin/store-config/pages/home', 'PUT');
        await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
        await assertResponse(saved);
        await page.waitForFunction(async () => !(await import('/src/modules/storefront/store/index.js')).useBuilderStore.getState().isDirty);
        await reload();
        await page.getByRole('button', { name: 'Edit', exact: true }).first().click();
        await page.getByRole('img', { name: 'Image preview', exact: true }).evaluate(i => i.decode());
        await page.getByRole('button', { name: 'Preview Saved Draft', exact: true }).click();
        await page.getByRole('dialog').getByRole('heading', { name: 'Welcome to Shopixy', exact: true }).waitFor();
        await page.screenshot({ path: `.artifacts/homepage-cert-${theme.id}-draft-preview.png` });
        await page.getByRole('button', { name: 'Close Preview', exact: true }).click();
        await live.goto('http://ahramstore.localhost:5174/');
        const unpublished = await live.evaluate(async reference => (await fetch(reference, { cache: 'no-store' })).status, asset.reference);
        if (unpublished !== 404) throw new Error(`Draft media is publicly accessible: ${unpublished}`);
        await page.getByRole('button', { name: 'Publish Homepage', exact: true }).click();
        const published = responseFor('/admin/store-config/pages/home/publish', 'POST');
        await page.getByRole('dialog').getByRole('button', { name: 'Publish Homepage', exact: true }).click();
        await assertResponse(published);
        await page.getByRole('dialog').waitFor({ state: 'hidden' });
        evidence.checkpoints.push('device upload', 'private draft denied anonymously', 'draft saved', 'draft reload', 'authenticated draft preview', 'homepage published');

        await go('http://localhost:5173/storefront/navigation?view=header');
        await page.getByLabel('Announcement text (English)', { exact: true }).fill(englishText);
        await page.getByLabel('Announcement text (Arabic)', { exact: true }).fill(arabicText);
        await page.getByLabel('Announcement link (optional)', { exact: true }).fill(`/products?announcement=${theme.id}`);
        await page.getByLabel('Show announcement bar', { exact: true }).check();
        await page.getByLabel(/Background color/).fill('#123457');
        await page.getByLabel(/Text color/).fill('#fffffd');
        const saveNavigation = page.getByRole('button', { name: 'Save Draft', exact: true });
        if (await saveNavigation.isEnabled()) await saveNavigation.click();
        await cleanHeader();
        await reload();
        await page.getByLabel('Announcement text (English)', { exact: true }).waitFor();
        if (await page.getByLabel('Announcement text (English)', { exact: true }).inputValue() !== englishText) throw new Error('Announcement draft lost on reload');
        await page.getByRole('button', { name: 'Publish Header and Footer Navigation', exact: true }).click();
        const navigationPublished = responseFor('/admin/cms/navigation/publish', 'POST');
        await page.getByRole('dialog').getByRole('button', { name: 'Publish Header and Footer Navigation', exact: true }).click();
        await assertResponse(navigationPublished);
        await page.getByRole('dialog').waitFor({ state: 'hidden' });
        evidence.checkpoints.push('header bilingual text/link/style authored', 'header draft reload', 'atomic Header/Footer publish');

        for (const language of ['en', 'ar']) {
            await live.setViewportSize({ width: 1600, height: 1000 });
            await live.reload();
            await live.locator('header').waitFor();
            await setLiveLanguage(language);
            for (const viewport of [{ width: 1600, height: 1000 }, { width: 390, height: 844 }]) {
                await live.setViewportSize(viewport);
                await live.waitForTimeout(350);
                await live.bringToFront();
                const expected = language === 'ar' ? arabicText : englishText;
                const region = live.getByRole('region', { name: language === 'ar' ? 'إعلان المتجر' : 'Store announcement', exact: true });
                await region.waitFor();
                await live.waitForFunction(expected => [...document.querySelectorAll('[role=region]')].some(region => region.textContent?.includes(expected)), expected);
                if (!(await region.innerText()).toLocaleLowerCase().includes(expected.toLocaleLowerCase())) throw new Error('Announcement localization mismatch');
                if (await region.getByRole('link').first().getAttribute('href') !== `/products?announcement=${theme.id}`) throw new Error('Announcement link mismatch');
                const pixels = await region.evaluate(e => ({ width: e.getBoundingClientRect().width, height: e.getBoundingClientRect().height, background: getComputedStyle(e).backgroundColor }));
                if (pixels.width <= 0 || pixels.height <= 0 || pixels.background !== 'rgb(18, 52, 87)') throw new Error('Announcement pixels/style missing');
                const image = await live.evaluate(async reference => {
                    const i = new Image(); i.src = reference; await i.decode();
                    const rendered = [...document.querySelectorAll('main img')].some(e => e.getAttribute('src') === reference) || [...document.querySelectorAll('main *')].some(e => getComputedStyle(e).backgroundImage.includes(reference));
                    return { rendered, width: i.naturalWidth, height: i.naturalHeight };
                }, asset.reference);
                if (!image.rendered || image.width !== 240) throw new Error('Published homepage asset not rendered');
                const overflow = await live.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1);
                if (overflow) throw new Error(`Storefront overflow: ${theme.id}/${language}/${viewport.width}`);
                await live.screenshot({ path: `.artifacts/homepage-cert-${theme.id}-${language}-${viewport.width}-storefront.png` });
                evidence.cells.push({ surface: 'Storefront', language, viewport, pixels, image, overflow });
            }

            await page.setViewportSize({ width: 1600, height: 1000 });
            await go('http://localhost:5173/storefront/navigation?view=header');
            await page.getByRole('tab').first().waitFor();
            await setAdminLanguage(language);
            for (const viewport of [{ width: 1600, height: 1000 }, { width: 390, height: 844 }]) {
                await page.setViewportSize(viewport);
                await page.waitForTimeout(350);
                await page.bringToFront();
                const preview = page.getByLabel(language === 'ar' ? 'معاينة الإعلان' : 'Announcement preview', { exact: true });
                await preview.scrollIntoViewIfNeeded();
                await page.screenshot({ path: `.artifacts/homepage-cert-${theme.id}-${language}-${viewport.width}-header.png` });
                if (await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)) throw new Error('Header editor overflow');
                evidence.cells.push({ surface: 'Header', language, viewport, overflow: false });
            }
            await go('http://localhost:5173/storefront/homepage');
            await page.getByRole('button', { name: language === 'ar' ? 'تعديل' : 'Edit', exact: true }).first().click();
            for (const viewport of [{ width: 1600, height: 1000 }, { width: 390, height: 844 }]) {
                await page.setViewportSize(viewport);
                await page.waitForTimeout(350);
                const image = page.getByRole('img', { name: language === 'ar' ? 'معاينة الصورة' : 'Image preview', exact: true });
                await image.evaluate(i => i.decode());
                await image.scrollIntoViewIfNeeded();
                await page.screenshot({ path: `.artifacts/homepage-cert-${theme.id}-${language}-${viewport.width}-editor.png` });
                if (await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)) throw new Error('Homepage editor overflow');
                evidence.cells.push({ surface: 'Homepage editor', language, viewport, overflow: false });
            }
        }

        await page.setViewportSize({ width: 1600, height: 1000 });
        await setAdminLanguage('en');
        await go('http://localhost:5173/storefront/navigation?view=header');
        await page.getByLabel('Show announcement bar', { exact: true }).uncheck();
        await page.getByRole('button', { name: 'Publish Header and Footer Navigation', exact: true }).click();
        const hiddenPublished = responseFor('/admin/cms/navigation/publish', 'POST');
        await page.getByRole('dialog').getByRole('button', { name: 'Publish Header and Footer Navigation', exact: true }).click();
        await assertResponse(hiddenPublished);
        await page.getByRole('dialog').waitFor({ state: 'hidden' });
        await live.reload();
        await live.locator('header').waitFor();
        await live.waitForFunction(async () => {
            const state = (await import('/src/stores/navigationStore.ts')).useNavigationStore.getState();
            return !state.loading && state.navigation?.header.announcement?.enabled === false;
        });
        await live.getByRole('region', { name: /Store announcement|إعلان المتجر/ }).waitFor({ state: 'hidden' });
        await page.getByLabel('Show announcement bar', { exact: true }).check();
        await page.getByRole('button', { name: 'Discard', exact: true }).click();
        await cleanHeader();
        await page.waitForFunction(async () => (await import('/src/modules/storefront/cms/store/index.js')).useCmsStore.getState().navigation.draft.announcement.enabled === false);
        if (await page.getByLabel('Show announcement bar', { exact: true }).isChecked()) throw new Error('Discard failed to restore published hidden state');
        await page.getByLabel('Show announcement bar', { exact: true }).check();
        await page.getByRole('button', { name: 'Publish Header and Footer Navigation', exact: true }).click();
        const restoredPublished = responseFor('/admin/cms/navigation/publish', 'POST');
        await page.getByRole('dialog').getByRole('button', { name: 'Publish Header and Footer Navigation', exact: true }).click();
        await assertResponse(restoredPublished);
        await page.getByRole('dialog').waitFor({ state: 'hidden' });
        evidence.checkpoints.push('visibility off published and region removed', 'discard restored published visibility', 'visibility on republished');
        await page.evaluate(({ index, evidence }) => {
            const results = JSON.parse(sessionStorage.getItem('homepage-theme-cert-results') || '[]');
            results.push(evidence);
            sessionStorage.setItem('homepage-theme-cert-results', JSON.stringify(results));
            sessionStorage.setItem('homepage-theme-cert-index', String(index + 1));
        }, { index, evidence });
        return { theme: theme.id, index, cells: evidence.cells.length, checkpoints: evidence.checkpoints, mediaReference: asset.reference };
    } catch (error) {
        await page.evaluate(({ theme, error, evidence }) => sessionStorage.setItem('homepage-theme-cert-error', JSON.stringify({ theme, error, evidence })), { theme: theme.id, error: String(error), evidence });
        await page.screenshot({ path: `.artifacts/homepage-cert-${theme.id}-failure-admin.png` });
        await live.screenshot({ path: `.artifacts/homepage-cert-${theme.id}-failure-live.png` });
        throw error;
    } finally {
        page.off('dialog', handleDialog);
    }
}

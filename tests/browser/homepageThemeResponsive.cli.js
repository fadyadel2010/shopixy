// Final screenshot/geometry sweep after rendering fixes. No content publication.
async page => {
    const live = page.context().pages().find(p => p.url().includes('ahramstore.localhost'));
    const themes = await page.evaluate(async () => (await import('/src/modules/storefront/themes/themeMarketplaceRegistry.js')).getAvailableMarketplaceThemes().map(t => ({ id: t.id, name: t.name })));
    const index = await page.evaluate(() => Number(sessionStorage.getItem('homepage-responsive-index') || '0'));
    const theme = themes[index];
    if (!theme) return { done: true };
    const evidence = [];
    await page.setViewportSize({ width: 1600, height: 1000 });
    await page.goto('http://localhost:5173/storefront/appearance/themes');
    await page.getByRole('heading', { name: /^(Themes|الثيمات)$/ }).waitFor();
    if ((await page.locator('html').getAttribute('lang')) !== 'en') await page.getByRole('button', { name: 'EN', exact: true }).click();
    const current = await page.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/store-config')).data.storefrontTheme);
    if (current !== theme.id) {
        await page.getByRole('heading', { name: theme.name, exact: true }).locator('xpath=../../..').getByRole('button', { name: 'Activate theme now', exact: true }).click();
        await page.getByRole('dialog').getByRole('button', { name: 'Activate theme now', exact: true }).click();
        await page.getByRole('dialog').waitFor({ state: 'hidden' });
    }
    for (const language of ['en', 'ar']) {
        await live.setViewportSize({ width: 1600, height: 1000 });
        await live.reload();
        await live.waitForFunction(async () => (await import('/src/stores/navigationStore.ts')).useNavigationStore.getState().navigation?.header.announcement?.text?.startsWith('CERT Collection Seven'));
        if ((await live.locator('html').getAttribute('lang')) !== language) await live.getByRole('button', { name: language === 'ar' ? 'Switch to Arabic' : 'Switch to English', exact: true }).click();
        for (const viewport of [{ width: 1600, height: 1000 }, { width: 390, height: 844 }]) {
            await live.setViewportSize(viewport);
            await live.waitForTimeout(400);
            await live.bringToFront();
            await live.screenshot({ path: `.artifacts/homepage-final-${theme.id}-${language}-${viewport.width}-storefront.png` });
            const overflow = await live.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1);
            if (overflow) throw new Error(`Storefront overflow ${theme.id}`);
            evidence.push({ surface: 'Storefront', theme: theme.id, language, viewport, overflow });
        }
        for (const route of ['appearance/themes', 'appearance/colors-typography', 'navigation?view=header', 'homepage']) {
            await page.setViewportSize({ width: 1600, height: 1000 });
            await page.goto(`http://localhost:5173/storefront/${route}`);
            await page.locator('main h1').waitFor();
            if ((await page.locator('html').getAttribute('lang')) !== language) await page.getByRole('button', { name: language === 'ar' ? 'AR' : 'EN', exact: true }).click();
            if (route === 'homepage') await page.getByRole('button', { name: language === 'ar' ? 'تعديل' : 'Edit', exact: true }).first().click();
            for (const viewport of [{ width: 1600, height: 1000 }, { width: 390, height: 844 }]) {
                await page.setViewportSize(viewport);
                await page.waitForTimeout(400);
                const image = route === 'homepage' ? page.getByRole('img', { name: language === 'ar' ? 'معاينة الصورة' : 'Image preview', exact: true }) : null;
                if (image) { await image.evaluate(i => i.decode()); await image.scrollIntoViewIfNeeded(); }
                const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1);
                if (overflow) throw new Error(`Admin overflow ${route}/${theme.id}/${language}/${viewport.width}`);
                if (route === 'homepage') {
                    const dimensions = await image.boundingBox();
                    if (dimensions.width < 250 && viewport.width === 390) throw new Error('Mobile image controls squeezed by desktop layout');
                }
                await page.bringToFront();
                await page.screenshot({ path: `.artifacts/homepage-final-${theme.id}-${language}-${viewport.width}-${route.split('?')[0].replace('/', '-')}.png` });
                evidence.push({ surface: route, theme: theme.id, language, viewport, overflow });
            }
        }
    }
    await page.evaluate(({ index, evidence }) => {
        const results = JSON.parse(sessionStorage.getItem('homepage-responsive-results') || '[]');
        results.push(...evidence);
        sessionStorage.setItem('homepage-responsive-results', JSON.stringify(results));
        sessionStorage.setItem('homepage-responsive-index', String(index + 1));
    }, { index, evidence });
    return { theme: theme.id, checkpoints: evidence.length };
}

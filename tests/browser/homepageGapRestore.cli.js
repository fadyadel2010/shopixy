async page => {
    const dialog = async d => { if (d.type() === 'beforeunload') await d.accept(); };
    page.on('dialog', dialog);
    try {
        page.setDefaultTimeout(20000);
        const original = await page.evaluate(() => JSON.parse(sessionStorage.getItem('gap-convergence-evidence')).original);
        const hero = original.config.livePages.home.sections.find(s => s.type === 'hero-banner');
        await page.setViewportSize({ width: 1600, height: 1000 });
        const currentTheme = await page.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/store-config')).data.storefrontTheme);
        if (currentTheme !== original.config.storefrontTheme) {
            await page.goto('http://localhost:5173/storefront/appearance/themes');
            if ((await page.locator('html').getAttribute('lang')) !== 'en') await page.getByRole('button', { name: 'EN', exact: true }).click();
            const name = await page.evaluate(async id => (await import('/src/modules/storefront/themes/themeMarketplaceRegistry.js')).getAvailableMarketplaceThemes().find(t => t.id === id).name, original.config.storefrontTheme);
            const card = page.getByRole('heading', { name, exact: true }).locator('xpath=../../..');
            await card.getByRole('button', { name: 'Activate theme now', exact: true }).click();
            const activated = page.waitForResponse(r => r.url().endsWith('/admin/store-config/theme/activate') && r.request().method() === 'POST');
            await page.getByRole('dialog').getByRole('button', { name: 'Activate theme now', exact: true }).click();
            if ((await activated).status() !== 200) throw new Error('Theme restoration failed');
            await page.getByRole('dialog').waitFor({ state: 'hidden' });
        }
        await page.goto('http://localhost:5173/storefront/homepage');
        await page.getByRole('button', { name: /^(Edit|تعديل)$/ }).first().waitFor();
        if ((await page.locator('html').getAttribute('lang')) !== 'en') await page.getByRole('button', { name: 'EN', exact: true }).click();
        if (await page.getByRole('button', { name: 'Discard', exact: true }).isEnabled()) await page.getByRole('button', { name: 'Discard', exact: true }).click();
        await page.waitForFunction(async () => !(await import('/src/modules/storefront/store/index.js')).useBuilderStore.getState().isDirty);
        await page.getByRole('button', { name: 'Edit', exact: true }).first().click();
        await page.getByRole('main').getByRole('textbox').first().fill(hero.settings.title);
        if (await page.getByRole('button', { name: 'Remove image', exact: true }).count()) await page.getByRole('button', { name: 'Remove image', exact: true }).click();
        await page.locator('input[type=url]').fill(hero.settings.backgroundImage);
        await page.getByRole('button', { name: 'Publish Homepage', exact: true }).click();
        const published = page.waitForResponse(r => r.url().endsWith('/admin/store-config/pages/home/publish') && r.request().method() === 'POST');
        await page.getByRole('dialog').getByRole('button', { name: 'Publish Homepage', exact: true }).click();
        if ((await published).status() !== 200) throw new Error('Homepage restoration failed');
        await page.getByRole('dialog').waitFor({ state: 'hidden' });
        await page.goto('http://localhost:5173/storefront/navigation?view=header');
        await page.getByRole('button', { name: 'Discard', exact: true }).waitFor();
        if (await page.getByRole('button', { name: 'Discard', exact: true }).isEnabled()) await page.getByRole('button', { name: 'Discard', exact: true }).click();
        await page.waitForFunction(async () => !(await import('/src/modules/storefront/cms/store/index.js')).useCmsStore.getState().isNavigationDirty);
        const resetAnnouncement = page.getByRole('button', { name: 'Use theme announcement default', exact: true });
        if (await resetAnnouncement.count()) await resetAnnouncement.click();
        const saveNavigation = page.getByRole('button', { name: 'Save Draft', exact: true });
        if (await saveNavigation.isEnabled()) await saveNavigation.click();
        await page.waitForFunction(async () => !(await import('/src/modules/storefront/cms/store/index.js')).useCmsStore.getState().isNavigationDirty);
        await page.waitForTimeout(400);
        const publish = page.getByRole('button', { name: 'Publish Header and Footer Navigation', exact: true });
        if (await publish.isEnabled()) {
            await publish.click();
            const response = page.waitForResponse(r => r.url().endsWith('/admin/cms/navigation/publish') && r.request().method() === 'POST');
            await page.getByRole('dialog').getByRole('button', { name: 'Publish Header and Footer Navigation', exact: true }).click();
            if ((await response).status() !== 200) throw new Error('Header restoration failed');
            await page.getByRole('dialog').waitFor({ state: 'hidden' });
        }
        const state = await page.evaluate(async () => { const api = (await import('/src/services/api.js')).default; return { config: (await api.get('/admin/store-config')).data, navigation: (await api.get('/admin/cms/navigation')).data }; });
        if (state.config.livePages.home.sections[0].settings.backgroundImage !== hero.settings.backgroundImage || state.navigation.live.announcement != null) throw new Error('Restoration verification failed');
        return { restored: true, theme: state.config.storefrontTheme, image: hero.settings.backgroundImage, title: hero.settings.title, announcement: state.navigation.live.announcement };
    } finally { page.off('dialog', dialog); }
}

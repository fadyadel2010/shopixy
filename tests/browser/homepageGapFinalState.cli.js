async page => {
    await page.setViewportSize({ width: 1600, height: 1000 });
    await page.goto('http://localhost:5173/storefront/homepage');
    await page.getByRole('button', { name: /^(Preview Saved Draft|معاينة المسودة المحفوظة)$/ }).waitFor();
    if ((await page.locator('html').getAttribute('lang')) !== 'en') await page.getByRole('button', { name: 'EN', exact: true }).click();
    const state = await page.evaluate(async () => {
        const api = (await import('/src/services/api.js')).default;
        const config = (await api.get('/admin/store-config')).data;
        const navigation = (await api.get('/admin/cms/navigation')).data;
        return { theme: config.storefrontTheme, image: config.livePages.home.sections[0].settings.backgroundImage,
            title: config.livePages.home.sections[0].settings.title,
            draftMatchesLive: JSON.stringify(config.pages.home.sections) === JSON.stringify(config.livePages.home.sections),
            announcementDefault: navigation.live.announcement == null && navigation.draft.announcement == null };
    });
    if (state.theme !== 'alizon' || state.title !== 'Welcome to Shopixy' || !state.draftMatchesLive || !state.announcementDefault) throw new Error('Merchant-visible state was not fully restored');
    await page.getByRole('button', { name: 'Preview Saved Draft', exact: true }).click();
    await page.frameLocator('iframe').getByRole('heading', { name: 'Welcome to Shopixy', exact: false }).waitFor();
    await page.frameLocator('iframe').locator('footer').waitFor();
    await page.screenshot({ path: '.artifacts/gap-final-restored-admin-preview.png' });
    await page.getByRole('button', { name: 'Close Preview', exact: true }).click();
    const live = await page.context().newPage();
    await live.setViewportSize({ width: 1600, height: 1000 });
    await live.goto('http://ahramstore.localhost:5174/');
    await live.getByRole('heading', { name: 'Welcome to Shopixy', exact: false }).waitFor();
    await live.locator('footer').waitFor();
    await live.waitForFunction(async () => (await import('/src/store/storeConfigStore.ts')).useStoreConfigStore.getState().config.storefrontTheme === 'alizon');
    await live.waitForTimeout(400);
    await live.screenshot({ path: '.artifacts/gap-final-restored-live.png' });
    await live.reload();
    await live.getByRole('heading', { name: 'Welcome to Shopixy', exact: false }).waitFor();
    await page.bringToFront();
    return { ...state, browserReloadPersisted: true };
}

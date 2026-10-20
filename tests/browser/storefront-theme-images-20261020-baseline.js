async page => {
    const root = '.ai/evidence/storefront-theme-images-20261020';
    const data = await page.evaluate(async () => {
        const { storeConfigService: service } = await import('/src/modules/storefront/services/storeConfigService.js');
        const [config, presets, history] = await Promise.all([
            service.getStoreConfig(), service.getThemePresets(), service.getVersionHistory(),
        ]);
        return { config, presets, history };
    });
    await page.screenshot({ path: `${root}/before-admin.png`, fullPage: true });
    return data;
}

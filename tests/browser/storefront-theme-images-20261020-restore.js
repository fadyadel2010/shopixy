async page => {
    await page.reload();
    await page.locator('#appearance-primary').waitFor();
    return page.evaluate(async () => {
        const { storeConfigService } = await import('/src/modules/storefront/services/storeConfigService.js');
        const before = await storeConfigService.getStoreConfig();
        // Restore the exact original Appearance draft: the same override string the
        // live branch carries, published under the original preset key.
        const payload = { ...JSON.parse(before.themeLiveOverridesJson), presetKey: before.themeLivePresetKey };
        const config = await storeConfigService.updateThemeOverrides(payload, { headers: { 'If-Match': `"${before.rowVersion}"` } });
        const history = await storeConfigService.getVersionHistory();
        return {
            rowVersion: config.rowVersion,
            draft: config.themeDraftOverridesJson,
            live: config.themeLiveOverridesJson,
            preset: config.themePresetKey,
            livePreset: config.themeLivePresetKey,
            theme: config.storefrontTheme,
            history: history.currentVersion,
        };
    });
}

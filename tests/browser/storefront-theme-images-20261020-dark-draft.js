async page => {
    await page.reload();await page.locator('#appearance-primary').waitFor();
    const before=await page.evaluate(async()=>{const {storeConfigService:s}=await import('/src/modules/storefront/services/storeConfigService.js');return {config:await s.getStoreConfig(),history:await s.getVersionHistory()};});
    const exact='{"colors":{"primary":"#2563eb","secondary":"#0f172a","background":"#ffffff","text":"#020617","muted":"#64748b"},"font":"Inter","borderRadius":8}';
    if(before.config.themeDraftOverridesJson!==exact||before.config.themeLiveOverridesJson!==exact||before.config.storefrontTheme!=='alizon')throw new Error('Configuration differs from preserved baseline; no QA write performed');
    await page.getByRole('button').filter({hasText:'Dark Minimal'}).click();
    await page.getByRole('button',{name:'Save Draft',exact:true}).click();
    await page.getByText('Appearance draft saved',{exact:true}).waitFor();
    const after=await page.evaluate(async()=>{const {storeConfigService:s}=await import('/src/modules/storefront/services/storeConfigService.js');return {config:await s.getStoreConfig(),history:await s.getVersionHistory()};});
    const passed=after.config.themeLiveOverridesJson===exact&&after.config.themeLivePresetKey==='default'&&after.config.draftTheme.colors.background==='#0f172a';
    return {before,after,passed,note:'Supported reversible Appearance draft only; no publication. The matrix restores the exact original draft in finally using this accepted RowVersion.'};
}

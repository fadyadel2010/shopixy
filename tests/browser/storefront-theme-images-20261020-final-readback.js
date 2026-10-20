async page => {
    await page.reload();await page.locator('#appearance-primary').waitFor();
    const admin=await page.evaluate(async()=>{const {storeConfigService:s}=await import('/src/modules/storefront/services/storeConfigService.js');return {config:await s.getStoreConfig(),history:await s.getVersionHistory()};});
    const sf=await page.context().newPage();
    try{
        await sf.goto('http://ahramstore.localhost:5174/');await sf.locator('[data-product-card-theme]').first().waitFor();
        const publicState=await sf.evaluate(async()=>{
            const {useStoreConfigStore}=await import('/src/store/storeConfigStore.ts');
            const {useAuthStore}=await import('/src/auth/store/authStore.ts');
            const {useCartStore}=await import('/src/store/cartStore.ts');
            await useCartStore.getState().fetchCart();
            const {config}=useStoreConfigStore.getState();const css=getComputedStyle(document.documentElement);
            return {storeName:config.storeName,tenantId:config.tenantId,theme:config.storefrontTheme,colors:config.theme.colors,
                tokens:Object.fromEntries(['primary','secondary','background','text','muted','on-primary','on-secondary'].map(k=>[k,css.getPropertyValue(`--color-${k}`).trim()])),
                customerEmail:useAuthStore.getState().customer?.email,
                cartItems:useCartStore.getState().cart?.items.map(i=>({productId:i.productId,variantId:i.variantId,quantity:i.quantity})),
                overflow:document.documentElement.scrollWidth>document.documentElement.clientWidth+1};
        });
        await sf.screenshot({path:'.ai/evidence/storefront-theme-images-20261020/final-restored-storefront.png',fullPage:true});
        const colors={primary:'#2563eb',secondary:'#0f172a',background:'#ffffff',text:'#020617',muted:'#64748b'};
        const failures=[];
        if(admin.config.storefrontTheme!=='alizon'||publicState.theme!=='alizon')failures.push('Original active theme mismatch');
        if(admin.config.themePresetKey!=='default'||admin.config.themeLivePresetKey!=='default')failures.push('Original preset mismatch');
        for(const [key,value] of Object.entries(colors))if(publicState.tokens[key]!==value)failures.push(`Public ${key} mismatch`);
        if(publicState.cartItems?.length!==0)failures.push('Original empty cart not restored');
        if(publicState.overflow)failures.push('Public restored page overflow');
        return {admin,publicState,failures,passed:failures.length===0};
    }finally{await sf.close();}
}

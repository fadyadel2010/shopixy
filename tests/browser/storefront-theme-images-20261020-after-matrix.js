async page => {
    page=await page.context().newPage();
    await page.goto('http://localhost:5173/storefront/appearance/colors-typography');
    await page.locator('#appearance-primary').waitFor();
    const root = '.ai/evidence/storefront-theme-images-20261020';
    const themes = await page.evaluate(async () => {
        const { getAvailableMarketplaceThemes } = await import('/src/modules/storefront/themes/themeMarketplaceRegistry.js');
        return getAvailableMarketplaceThemes().map(theme => theme.id);
    });
    const paletteName = (await page.locator('#appearance-background').inputValue()).toLowerCase() === '#0f172a' ? 'dark-minimal' : 'default';
    const expectedColors=await page.locator('input[id^="appearance-"][type="text"]').evaluateAll(ns=>Object.fromEntries(ns.filter(n=>['primary','secondary','background','text','muted'].some(k=>n.id===`appearance-${k}`)).map(n=>[n.id.replace('appearance-',''),n.value.toLowerCase()])));
    const qaBaseline = paletteName === 'dark-minimal' ? await page.evaluate(async()=>{
        const {storeConfigService:s}=await import('/src/modules/storefront/services/storeConfigService.js');
        const c=await s.getStoreConfig();return {rowVersion:c.rowVersion,payload:{...JSON.parse(c.themeLiveOverridesJson),presetKey:c.themeLivePresetKey}};
    }) : null;
    const results = [], failures = [];
    let restoration = null;
    let stage = {};
    const storefront = await page.context().newPage();
    await storefront.emulateMedia({ reducedMotion:'reduce' });
    try {
        for (const theme of themes) {
            stage={theme};
            // Re-enter the supported auth bootstrap between long theme batches.
            // This avoids treating an expired in-memory Admin token as a theme failure.
            await page.reload();await page.locator('#appearance-primary').waitFor();
            const preview = await page.evaluate(async id => {
                const { storeConfigService } = await import('/src/modules/storefront/services/storeConfigService.js');
                return storeConfigService.createThemePreviewLink(id);
            }, theme);
            const navigate = async path => {
                const url = await page.evaluate(({href,path}) => {const u=new URL(href);u.pathname=path;return u.toString();}, {href:preview.previewUrl,path});
                await storefront.goto(url);
                await storefront.waitForFunction(async id=>{const {useStoreConfigStore}=await import('/src/store/storeConfigStore.ts');return useStoreConfigStore.getState().isLoaded&&useStoreConfigStore.getState().config.storefrontTheme===id;},theme);
                await storefront.locator('main, form').first().waitFor({timeout:30000});
            };
            for (const route of ['/', '/products', '/products/cert-media-variable-20261007','/cart','/checkout','/login','/register']) {
                stage={theme,route};
                await navigate(route);
                if (['/', '/products', '/products/cert-media-variable-20261007'].includes(route)) {
                    await storefront.locator('[data-product-card-theme]').first().waitFor();
                }
                for (const [width,height] of (route === '/' || route.includes('cert-media') ? [[1440,1000],[1280,800],[768,900],[390,844]] : [[1440,1000],[390,844]])) {
                    await storefront.setViewportSize({width,height});
                    await storefront.evaluate(async()=>{await document.fonts.ready;await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));await Promise.all(document.getAnimations().filter(a=>Number.isFinite(a.effect?.getComputedTiming().endTime)).map(a=>a.finished.catch(()=>{})));});
                    await storefront.evaluate(() => window.scrollTo(0,0));
                    if (route === '/' || route.includes('cert-media')) {
                        await storefront.locator('h1').first().waitFor();
                    }
                    const data = await storefront.evaluate(({theme,route,width,height}) => {
                        const sample = element => {
                            const b=element.getBoundingClientRect(),s=getComputedStyle(element);
                            return {tag:element.tagName, width:b.width,height:b.height,color:s.color,background:s.backgroundColor,
                                border:s.borderColor,position:s.position,fit:s.objectFit,aspectRatio:s.aspectRatio};
                        };
                        const s=getComputedStyle(document.documentElement);
                        return {theme,route,width,height,actualPath:location.pathname,direction:document.documentElement.dir,
                            renderedCardThemes:[...new Set([...document.querySelectorAll('[data-product-card-theme]')].map(el=>el.dataset.productCardTheme))],
                            tokens:Object.fromEntries(['primary','secondary','background','text','muted','primary-text','surface','border'].map(k=>[k,s.getPropertyValue(`--color-${k}`).trim()])),
                            overflow:document.documentElement.scrollWidth>document.documentElement.clientWidth+1,
                            pageWidth:document.documentElement.clientWidth,scrollWidth:document.documentElement.scrollWidth,
                            overflowElements:[...document.querySelectorAll('main *')].map(el=>{const b=el.getBoundingClientRect();return {class:el.className,left:b.left,right:b.right,width:b.width};}).filter(b=>b.right>document.documentElement.clientWidth+1||b.left< -1).slice(0,15),
                            surfaces:[...document.querySelectorAll('header, footer, h1, h2, input, [data-product-card-theme]')].filter(n=>n.getBoundingClientRect().width>0).slice(0,30).map(sample),
                            frames:[...document.querySelectorAll('.sf-media')].map(el=>({role:el.dataset.mediaRole|| (el.className.includes('product-detail')?'product-detail':'unknown'),...sample(el),
                                images:[...el.querySelectorAll(':scope > img')].map(img=>({...sample(img),naturalWidth:img.naturalWidth,naturalHeight:img.naturalHeight,complete:img.complete,fallback:img.src.includes('placeholder')||img.src.startsWith('data:')}))}))};
                    },{theme,route,width,height});
                    if(data.overflow) failures.push({theme,route,width,reason:'horizontal overflow'});
                    for(const [key,value] of Object.entries(expectedColors))if(data.tokens[key].toLowerCase()!==value)failures.push({theme,route,width,reason:`${key} palette mismatch`,expected:value,actual:data.tokens[key]});
                    for (const frame of data.frames) {
                        if(['product-card','product-detail'].includes(frame.role) && Math.abs(frame.width-frame.height)>2) failures.push({theme,route,width,reason:'not square',frame});
                        if(frame.role==='product-detail' && frame.height>Math.min(600,height*.65)+2) failures.push({theme,route,width,reason:'detail exceeds bounds',frame});
                        if(frame.role==='hero' && frame.height>Math.min(560,height*.65)+2) failures.push({theme,route,width,reason:'hero exceeds bounds',frame});
                        for(const img of frame.images) {
                            if(['product-card','product-detail'].includes(frame.role) && img.fit!=='contain') failures.push({theme,route,width,reason:'product crop',img});
                        }
                    }
                    results.push(data);
                    if([1440,390].includes(width)) await storefront.screenshot({path:`${root}/after-${paletteName}-${theme}-${width}-${route==='/'?'home':route.split('/').pop()}.png`,fullPage:route==='/',animations:'disabled',timeout:60000});
                }
            }
            // Refresh and Arabic are actual runtime operations, not DOM direction spoofing.
            await navigate('/');
            await storefront.evaluate(async()=>{const {useLanguageStore}=await import('/src/i18n/languageStore.ts');useLanguageStore.getState().setLanguage('ar');});
            await storefront.reload();
            await storefront.locator('header').first().waitFor();
            const rtl=await storefront.evaluate(()=>document.documentElement.dir);
            results.push({theme,rtl,refresh:true});
            if(rtl!=='rtl')failures.push({theme,reason:'Arabic refresh direction mismatch',rtl});
            await storefront.screenshot({path:`${root}/after-${paletteName}-${theme}-rtl.png`,animations:'disabled',timeout:60000});
            await storefront.evaluate(async()=>{const {useLanguageStore}=await import('/src/i18n/languageStore.ts');useLanguageStore.getState().setLanguage('en');});
        }
    } catch(error) {failures.push({...stage,reason:error.message});}
    finally {
        await storefront.close();
        if(qaBaseline){
            try{
                restoration=await page.evaluate(async baseline=>{
                    const {storeConfigService:s}=await import('/src/modules/storefront/services/storeConfigService.js');
                    const config=await s.updateThemeOverrides(baseline.payload,{headers:{'If-Match':`"${baseline.rowVersion}"`}});
                    return {config,history:await s.getVersionHistory()};
                },qaBaseline);
                await page.reload();await page.locator('#appearance-primary').waitFor();
            }catch(error){failures.push({reason:`QA draft restoration failed: ${error.message}`});}
        }
        await page.close();
    }
    return {paletteName,results,failures,restoration,passed:failures.length===0};
}

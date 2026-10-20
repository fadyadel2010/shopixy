async page => {
    page=await page.context().newPage();
    await page.goto('http://localhost:5173/storefront/appearance/colors-typography');
    // The Admin session restore can take over a minute while the datastore is slow.
    await page.locator('#appearance-primary').waitFor({timeout:180000});
    const root='.ai/evidence/storefront-theme-images-20261020';
    const themes=['modern','minimal','fashion','pearl','phoenix','hype','alizon','sharp','cosmetics','seven'];
    const results=[],failures=[];
    const storefront=await page.context().newPage();
    await storefront.emulateMedia({reducedMotion:'reduce'});
    // Only image bytes are task-owned fixtures. Real API/config/product renderers
    // and all persisted records remain untouched. No color or commerce mocks.
    let fixture='portrait';
    const bodies={
        portrait:'<svg xmlns="http://www.w3.org/2000/svg" width="300" height="2400"><rect width="300" height="2400" fill="#1e293b"/><text x="20" y="1200" fill="white">QA portrait</text></svg>',
        landscape:'<svg xmlns="http://www.w3.org/2000/svg" width="2400" height="300"><rect width="2400" height="300" fill="#14532d"/></svg>',
        square:'<svg xmlns="http://www.w3.org/2000/svg" width="800" height="800"><circle cx="400" cy="400" r="300" fill="#78350f"/></svg>',
        padded:'<svg xmlns="http://www.w3.org/2000/svg" width="800" height="800"><rect x="300" y="200" width="200" height="400" fill="#7c3aed"/></svg>',
        large:'<svg xmlns="http://www.w3.org/2000/svg" width="12000" height="8000"><rect width="12000" height="8000" fill="#881337"/></svg>',
    };
    let release, pending=[], delayReleased=false, stage={};
    await storefront.route(/\/api\/upload\/product-media\/|m\.media-amazon\.com\/images\//,async route=>{
        if(fixture==='delayed'&&!delayReleased) await new Promise(resolve=>pending.push(resolve));
        if(fixture==='failed') return route.fulfill({status:404,body:''});
        await route.fulfill({status:200,contentType:'image/svg+xml',body:bodies[fixture]||bodies.portrait});
    });
    const sample=()=>storefront.evaluate(()=>({overflow:document.documentElement.scrollWidth>document.documentElement.clientWidth+1,
        frames:[...document.querySelectorAll('.sf-media')].map(el=>{const b=el.getBoundingClientRect(),img=el.querySelector(':scope > img');return {
            role:el.dataset.mediaRole||'product-detail',width:b.width,height:b.height,
            image:img?{width:img.getBoundingClientRect().width,height:img.getBoundingClientRect().height,fit:getComputedStyle(img).objectFit,
                naturalWidth:img.naturalWidth,naturalHeight:img.naturalHeight,complete:img.complete,fallback:img.src.includes('placeholder')||img.src.startsWith('data:')}:null};})}));
    try{
        for(const theme of themes){
            await page.reload();await page.locator('#appearance-primary').waitFor({timeout:180000});
            const preview=await page.evaluate(async id=>{const {storeConfigService}=await import('/src/modules/storefront/services/storeConfigService.js');return storeConfigService.createThemePreviewLink(id);},theme);
            for(const route of ['/','/products/cert-media-variable-20261007']){
                const url=await page.evaluate(({href,path})=>{const u=new URL(href);u.pathname=path;return u.toString();},{href:preview.previewUrl,path:route});
                for(const [width,height] of [[1440,1000],[390,844]]){
                    await storefront.setViewportSize({width,height});
                    let baseline;
                    for(fixture of ['portrait','landscape','square','padded','large','failed','delayed']){
                        stage={theme,route,width,fixture};delayReleased=false;
                        await storefront.goto(url,{waitUntil:'domcontentloaded'});
                        await storefront.waitForFunction(async id=>{const {useStoreConfigStore}=await import('/src/store/storeConfigStore.ts');return useStoreConfigStore.getState().isLoaded&&useStoreConfigStore.getState().config.storefrontTheme===id;},theme);
                        await storefront.locator('h1').first().waitFor({timeout:60000});
                        if(route!=='/') await storefront.locator('.sf-media--product-detail').waitFor();
                        await storefront.locator('.sf-media').first().waitFor();
                        // Product API completion is independent of image completion.
                        // Compare the same rendered section set, not a skeleton to a grid.
                        await storefront.waitForFunction(expected=>document.querySelectorAll('[data-product-card-theme]').length>=expected,route==='/'?4:2);
                        await storefront.evaluate(async()=>{await document.fonts.ready;await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));});
                        await storefront.evaluate(()=>{document.querySelectorAll('.sf-media img').forEach(i=>i.loading='eager');});
                        const before=await sample();
                        if(fixture==='delayed'){
                            delayReleased=true;
                            release=pending.splice(0);release.forEach(resolve=>resolve());
                        }
                        for(const img of await storefront.locator('.sf-media img').all())await img.scrollIntoViewIfNeeded();
                        await storefront.waitForFunction(()=>[...document.querySelectorAll('.sf-media img')].every(i=>{i.loading='eager';return i.complete&&i.naturalWidth>0;}),{},{timeout:20000});
                        const loaded=await sample();
                        const dimensions=loaded.frames.map(f=>[f.role,Math.round(f.width),Math.round(f.height)]);
                        baseline ||= dimensions;
                        if(JSON.stringify(dimensions)!==JSON.stringify(baseline))failures.push({theme,route,width,fixture,reason:'source-dependent dimensions',baseline,dimensions});
                        if(loaded.overflow)failures.push({theme,route,width,fixture,reason:'overflow'});
                        if(fixture==='delayed'&&JSON.stringify(before.frames.map(f=>[Math.round(f.width),Math.round(f.height)]))!==JSON.stringify(loaded.frames.map(f=>[Math.round(f.width),Math.round(f.height)])))failures.push({theme,route,width,reason:'load layout shift'});
                        results.push({theme,route,width,height,fixture,before,loaded});
                        if(['portrait','landscape','failed'].includes(fixture)&&width===390)await storefront.screenshot({path:`${root}/after-fixture-${theme}-${route==='/'?'home':'detail'}-${fixture}.png`,animations:'disabled',timeout:60000});
                    }
                }
            }
        }
    }catch(error){failures.push({...stage,reason:error.message,diagnostic:await storefront.locator('.sf-media img').evaluateAll(ns=>ns.map(i=>({src:i.src,complete:i.complete,naturalWidth:i.naturalWidth,loading:i.loading}))) });}
    finally{pending.forEach(resolve=>resolve());await storefront.close();await page.close();}
    return {results,failures,passed:failures.length===0};
}

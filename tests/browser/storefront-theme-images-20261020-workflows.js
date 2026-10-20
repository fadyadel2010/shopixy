async page => {
    const root='.ai/evidence/storefront-theme-images-20261020';
    const read=()=>page.evaluate(async()=>{const {storeConfigService:s}=await import('/src/modules/storefront/services/storeConfigService.js');return {config:await s.getStoreConfig(),history:await s.getVersionHistory()};});
    const original={presetKey:'default',colors:{primary:'#2563eb',secondary:'#0f172a',background:'#ffffff',text:'#020617',muted:'#64748b'},font:'Inter',borderRadius:8};
    const results=[], failures=[];
    const values=()=>page.locator('input[id^="appearance-"][type="text"]').evaluateAll(ns=>Object.fromEntries(ns.map(n=>[n.id.replace('appearance-',''),n.value])));
    const save=async()=>{await page.getByRole('button',{name:'Save Draft',exact:true}).click();await page.getByText('Appearance draft saved',{exact:true}).waitFor();};
    const restore=async()=>page.evaluate(async original=>{const {storeConfigService:s}=await import('/src/modules/storefront/services/storeConfigService.js');let c=await s.getStoreConfig();c=await s.updateThemeOverrides(original,{headers:{'If-Match':`"${c.rowVersion}"`}});if(c.themeLivePresetKey!=='default'||JSON.stringify(c.liveTheme.colors)!==JSON.stringify(original.colors))c=await s.publishTheme({headers:{'If-Match':`"${c.rowVersion}"`}});return {config:c,history:await s.getVersionHistory()};},original);
    let final;
    try{
        await page.goto('http://localhost:5173/storefront/appearance/colors-typography');
        await page.locator('#appearance-primary').waitFor();
        const initial=await read();results.push({stage:'initial',...initial});
        for(const [i,theme] of ['modern','minimal','fashion','pearl','phoenix','hype','alizon','sharp','cosmetics','seven'].entries()){
            const previous=await values(), qa=i%2===0?'#7c3aed':'#a78bfa';
            await page.locator('#appearance-primary').fill(qa);
            const edited=await values();
            for(const key of ['secondary','background','text','muted'])if(edited[key]!==previous[key])failures.push({theme,reason:`primary changed ${key}`});
            await save();await page.reload();await page.locator('#appearance-primary').waitFor();
            const reopened=await values();if(reopened.primary!==qa)failures.push({theme,reason:'save/reopen mismatch'});
            const preview=await page.evaluate(async id=>{const {storeConfigService:s}=await import('/src/modules/storefront/services/storeConfigService.js');return s.createThemePreviewLink(id);},theme);
            const sf=await page.context().newPage();
            await sf.goto(preview.previewUrl);await sf.locator('header').waitFor();
            const actual=await sf.evaluate(()=>getComputedStyle(document.documentElement).getPropertyValue('--color-primary').trim());
            if(actual.toLowerCase()!==qa)failures.push({theme,reason:'saved draft/runtime mismatch',actual,qa});
            await sf.close();
            results.push({stage:'theme-save-reopen-preview',theme,previous,edited,reopened,actual,rowVersion:(await read()).config.rowVersion});
        }
        const saved=await values();
        await page.locator('#appearance-primary').fill('#123456');
        await page.getByRole('button',{name:'Discard',exact:true}).click();
        results.push({stage:'discard',saved,restored:await values()});
        if(JSON.stringify(saved)!==JSON.stringify(await values()))failures.push({reason:'discard mismatch'});
        await page.locator('#appearance-primary').fill('#654321');
        await page.route('**/api/admin/store-config/theme/overrides',route=>route.abort('failed'));
        await page.getByRole('button',{name:'Save Draft',exact:true}).click();
        await page.getByText('Appearance draft not saved',{exact:true}).waitFor();
        await page.unroute('**/api/admin/store-config/theme/overrides');
        const failed=await values();results.push({stage:'failed-save',candidate:failed,persisted:(await read()).config.draftTheme});
        if(failed.primary!=='#654321')failures.push({reason:'save failure erased draft'});
        await page.getByRole('button',{name:'Discard',exact:true}).click();
        // Publication is narrowly Appearance-only and always restored in finally.
        await page.getByRole('button').filter({hasText:'Dark Minimal'}).click();
        await page.locator('#appearance-primary').fill('#7c3aed');
        await page.getByRole('button',{name:'Publish Colors & Typography',exact:true}).click();
        await page.getByRole('dialog').getByRole('button',{name:'Publish Colors & Typography',exact:true}).click();
        await page.getByText('Colors & Typography published',{exact:true}).waitFor();
        const published=await read();results.push({stage:'published',...published});
        await page.screenshot({path:`${root}/after-admin-published-qa.png`,fullPage:true});
        const publicPage=await page.context().newPage();await publicPage.goto('http://ahramstore.localhost:5174/');await publicPage.locator('header').waitFor();
        results.push({stage:'actual-public',tokens:await publicPage.evaluate(()=>Object.fromEntries(['primary','background','text'].map(k=>[k,getComputedStyle(document.documentElement).getPropertyValue(`--color-${k}`).trim()])))});
        await publicPage.screenshot({path:`${root}/after-live-alizon-qa.png`,fullPage:true});await publicPage.close();
    }catch(error){failures.push({reason:error.message});}
    finally{
        await page.unroute('**/api/admin/store-config/theme/overrides');
        final=await restore();
        await page.reload();await page.locator('#appearance-primary').waitFor();
        await page.screenshot({path:`${root}/after-admin-restored.png`,fullPage:true});
    }
    return {results,failures,final,passed:failures.length===0};
}

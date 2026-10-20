async page => {
    page=await page.context().newPage();
    await page.goto('http://localhost:5173/storefront/appearance/colors-typography');
    await page.locator('#appearance-primary').waitFor();
    const results=[];
    try{
    for(const theme of ['modern','minimal','fashion','pearl','phoenix','hype','alizon','sharp','cosmetics','seven']){
        await page.reload();await page.locator('#appearance-primary').waitFor();
        const preview=await page.evaluate(async id=>{const {storeConfigService:s}=await import('/src/modules/storefront/services/storeConfigService.js');return s.createThemePreviewLink(id);},theme);
        const sf=await page.context().newPage();
        try{
            await sf.goto(preview.previewUrl);await sf.locator('header').waitFor();
            for(const path of ['/','/products/cert-media-variable-20261007','/login','/register']){
                const url=await page.evaluate(({href,path})=>{const u=new URL(href);u.pathname=path;return u.toString();},{href:preview.previewUrl,path});
                await sf.goto(url);await sf.locator('h1').first().waitFor();
                await sf.waitForFunction(async id=>{const {useStoreConfigStore}=await import('/src/store/storeConfigStore.ts');return useStoreConfigStore.getState().isLoaded&&useStoreConfigStore.getState().config.storefrontTheme===id;},theme);
                await sf.evaluate(async()=>{await document.fonts.ready;await Promise.all(document.getAnimations().filter(a=>Number.isFinite(a.effect?.getComputedTiming().endTime)).map(a=>a.finished.catch(()=>{})));});
                results.push(await sf.evaluate(({theme,path})=>{
                    const parse=color=>{const m=color.match(/[\d.]+/g)?.map(Number);return m&&m.length>=3?[m[0],m[1],m[2],m[3]??1]:null;};
                    const over=(fg,bg)=>fg.slice(0,3).map((c,i)=>c*fg[3]+bg[i]*(1-fg[3])).concat(1);
                    const lum=rgb=>rgb.slice(0,3).map(c=>{const s=c/255;return s<=.04045?s/12.92:((s+.055)/1.055)**2.4;}).reduce((n,c,i)=>n+c*[.2126,.7152,.0722][i],0);
                    const checked=[],exceptions=[];
                    for(const el of document.querySelectorAll('h1,h2,h3,p,label,a,button,input,select,span')){
                        const b=el.getBoundingClientRect(),s=getComputedStyle(el);
                        const text=[...el.childNodes].filter(n=>n.nodeType===Node.TEXT_NODE).map(n=>n.textContent).join('').trim()||(['INPUT','SELECT'].includes(el.tagName)?el.value:'');
                        if(!b.width||!b.height||s.visibility==='hidden'||s.opacity==='0'||!text||el.closest('[aria-hidden="true"]'))continue;
                        let bg=[255,255,255,1],layers=[],image=false;
                        for(let parent=el;parent;parent=parent.parentElement){const ps=getComputedStyle(parent);layers.push(parse(ps.backgroundColor));image ||= ps.backgroundImage!=='none';}
                        // Decorative category art/overlays are absolutely positioned siblings,
                        // not ancestor backgrounds. Their actual pixels need manual review.
                        const article=el.closest('article');
                        image ||= !!article&&[...article.querySelectorAll('*')].some(n=>getComputedStyle(n).backgroundImage!=='none');
                        for(const layer of layers.reverse())if(layer)bg=over(layer,bg);
                        const fg=parse(s.color);if(!fg)continue;
                        const luminances=[lum(over(fg,bg)),lum(bg)].sort((a,b)=>b-a),ratio=(luminances[0]+.05)/(luminances[1]+.05);
                        const large=parseFloat(s.fontSize)>=24||(parseFloat(s.fontSize)>=18.66&&Number(s.fontWeight)>=700),target=large?3:4.5;
                        const row={tag:el.tagName,text:text.slice(0,80),classes:el.className,color:s.color,background:bg.slice(0,3).map(Math.round),ratio:Number(ratio.toFixed(2)),target,imageBackground:image,disabled:el.closest(':disabled')!==null};
                        checked.push(row);if(ratio<target&&!row.disabled)exceptions.push(row);
                    }
                    return {theme,path,checked,exceptions};
                },{theme,path}));
            }
        }finally{await sf.close();}
    }
    }finally{await page.close();}
    return {results,note:'Computed solid-background contrast; image/gradient backgrounds flagged for manual review, not certified from pixel-free DOM math.'};
}

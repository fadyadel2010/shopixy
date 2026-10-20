async page => {
    await page.reload();await page.locator('#appearance-primary').waitFor();
    const config=await page.evaluate(async()=>{const {storeConfigService:s}=await import('/src/modules/storefront/services/storeConfigService.js');return s.getPreviewConfig();});
    const iframe=page.locator('iframe').first();await iframe.waitFor();
    const frame=page.frameLocator('iframe').first();await frame.locator('[data-admin-draft-preview]').waitFor();
    // frameLocator has no evaluate(); use the real child frame object.
    const previewFrame=()=>page.frames().find(f=>f.url().includes('/draft-preview.html'));
    const results=[],failures=[],shots=[];
    let stage={};
    let dimension=[300,2400];
    await page.context().route('**/__task-theme-image-fixture__/*',route=>route.fulfill({status:200,contentType:'image/svg+xml',body:`<svg xmlns="http://www.w3.org/2000/svg" width="${dimension[0]}" height="${dimension[1]}"><rect width="100%" height="100%" fill="#2563eb"/></svg>`}));
    try{
        for(const theme of ['modern','minimal','fashion','pearl','phoenix','hype','alizon','sharp','cosmetics','seven']){
            for(const width of [1440,390]){
                await iframe.evaluate((el,width)=>{el.style.width=`${width}px`;el.style.transform='none';},width);
                let baseline;
                for(const [fixture,size] of [['portrait',[300,2400]],['landscape',[2400,300]],['square',[800,800]],['large',[12000,8000]]]){
                    stage={theme,width,fixture};
                    dimension=size;
                    const candidate={...config,storefrontTheme:theme,pages:{...config.pages,home:{...config.pages.home,sections:[
                        {id:'qa-content-only',type:'image-block',enabled:true,order:0,settings:{imageUrl:`/__task-theme-image-fixture__/${theme}-${width}-${fixture}.svg`,altText:'Task-owned QA image',caption:'Preview only — not saved',aspectRatio:'16:9',maxWidth:'wide'}},
                    ]}}};
                    await iframe.evaluate((el,config)=>{const u=new URL(el.src);el.contentWindow.postMessage({protocol:'shopixy.admin-draft-preview.v1',type:'apply',nonce:u.hash.slice(1),config,language:'en'},u.origin);},candidate);
                    const image=frame.locator(`.sf-media--content img[src$="${theme}-${width}-${fixture}.svg"]`);
                    // The preview re-renders on every applied candidate. Re-apply when the
                    // expected source is not mounted yet instead of failing the scenario.
                    let mounted=false;
                    for(let attempt=0;attempt<3&&!mounted;attempt++){
                        try{await image.waitFor({timeout:15000});mounted=true;}
                        catch{await iframe.evaluate((el,config)=>{const u=new URL(el.src);el.contentWindow.postMessage({protocol:'shopixy.admin-draft-preview.v1',type:'apply',nonce:u.hash.slice(1),config,language:'en'},u.origin);},candidate);}
                    }
                    if(!mounted)throw new Error(`Task content image did not mount for ${theme}-${width}-${fixture}`);
                    // Measure inside the frame with retries: the preview re-renders the
                    // section on every applied candidate, so a locator can detach.
                    let data=null;
                    for(let attempt=0;attempt<5&&!data;attempt++){
                        try{
                            data=await previewFrame().evaluate(({theme,width,fixture})=>{
                                const img=[...document.querySelectorAll('.sf-media--content img')].find(n=>n.src.endsWith(`${theme}-${width}-${fixture}.svg`));
                                if(!img)return null;
                                const frame=img.closest('.sf-media');
                                const b=frame.getBoundingClientRect();
                                return {width:b.width,height:b.height,fit:getComputedStyle(img).objectFit,position:getComputedStyle(img).position,naturalWidth:img.naturalWidth,naturalHeight:img.naturalHeight,overflow:document.documentElement.scrollWidth>document.documentElement.clientWidth+1};
                            },{theme,width,fixture});
                        }catch{await page.waitForTimeout(400);}
                    }
                    if(!data)throw new Error(`Task content frame could not be measured for ${theme}-${width}-${fixture}`);
                    const dimensions=[Math.round(data.width),Math.round(data.height)];baseline ||= dimensions;
                    if(JSON.stringify(baseline)!==JSON.stringify(dimensions)||data.height>640||data.fit!=='contain'||data.position!=='absolute'||data.overflow)failures.push({theme,width,fixture,data,baseline});
                    results.push({theme,width,fixture,...data});
                    if(fixture==='portrait'){
                        // Screenshots are illustrative only; the JSON measurements above
                        // are the assertions. A re-render must not fail the run.
                        try{await image.screenshot({path:`.ai/evidence/storefront-theme-images-20261020/after-content-preview-${theme}-${width}.png`,animations:'disabled',timeout:30000});}catch{shots.push({theme,width});}
                    }
                }
            }
        }
    }catch(error){failures.push({...stage,reason:error.message});}
    finally{
        await page.context().unroute('**/__task-theme-image-fixture__/*');
        await page.reload();await page.locator('#appearance-primary').waitFor();
    }
    return {results,failures,passed:failures.length===0,screenshotSkipped:shots,note:'Actual authenticated runtime preview and shared section renderer; task-owned candidate and image bytes only. No saved or published content changes.'};
}

async page => {
    await page.goto('http://localhost:5173/storefront/appearance/colors-typography');
    await page.locator('#appearance-primary').waitFor();
    const original=await page.locator('#appearance-primary').inputValue();
    const frame=page.frameLocator('iframe[title*="Storefront"], iframe[title*="معاينة المتجر"]');
    const sample=()=>page.evaluate(()=>({background:getComputedStyle(document.body).backgroundColor,
        text:getComputedStyle(document.querySelector('#appearance-primary')).color,
        token:document.documentElement.style.getPropertyValue('--color-primary')}));
    const before=await sample(),failures=[];
    let local,discarded;
    try{
        await frame.locator('[data-admin-draft-preview]').waitFor();
        await page.locator('#appearance-primary').fill('#6d28d9');
        await frame.locator('html').evaluate(async el=>{
            await new Promise((resolve,reject)=>{
                let count=0;const check=()=>{if(getComputedStyle(el).getPropertyValue('--color-primary').trim()==='#6d28d9')resolve();else if(count++>600)reject(new Error('Preview did not accept local palette'));else requestAnimationFrame(check);};check();
            });
        });
        local=await frame.locator('html').evaluate(el=>Object.fromEntries(['primary','secondary','background','text','muted'].map(k=>[k,getComputedStyle(el).getPropertyValue(`--color-${k}`).trim()])));
        const adminAfter=await sample();
        if(JSON.stringify(before)!==JSON.stringify(adminAfter))failures.push({reason:'Storefront candidate leaked into Admin',before,adminAfter});
        if(local.primary!=='#6d28d9')failures.push({reason:'local candidate preview mismatch'});
        await page.screenshot({path:'.ai/evidence/storefront-theme-images-20261020/after-editor-live-local-preview.png',fullPage:true});
        await page.getByRole('button',{name:'Discard',exact:true}).click();
        await frame.locator('html').evaluate(async(el,expected)=>{await new Promise((resolve,reject)=>{let count=0;const check=()=>getComputedStyle(el).getPropertyValue('--color-primary').trim()===expected?resolve():count++>600?reject(new Error('Discard not reflected')):requestAnimationFrame(check);check();});},original);
        discarded=await frame.locator('html').evaluate(el=>getComputedStyle(el).getPropertyValue('--color-primary').trim());
    }finally{
        if(await page.getByRole('button',{name:'Discard',exact:true}).isEnabled())await page.getByRole('button',{name:'Discard',exact:true}).click();
    }
    return {before,local,discarded,original,failures,passed:failures.length===0};
}

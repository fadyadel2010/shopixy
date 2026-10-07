// Read-only headed certification of the disposable product surfaces.
async page => {
    const simple = '15e044bc-eaa1-c905-31a4-658aac4ad14b';
    const variable = '03144bba-1880-c1d6-3062-997a1006feca';
    const selector = `main img[src*="${simple}"], main img[src*="${variable}"]`;
    const evidence = [];
    const failures = [];
    const listener = response => {
        if (response.url().includes('/api/upload/product-media/') && response.status() !== 200)
            failures.push({url:response.url(),status:response.status()});
    };
    page.on('response', listener);
    try {
        for (const viewport of [{width:1440,height:1000},{width:390,height:844}]) {
            await page.setViewportSize(viewport);
            for (const language of ['en','ar']) {
                await page.goto('http://localhost:5173/products');
                await page.getByRole('button',{name:/^(AR|EN)$/}).waitFor();
                if ((await page.locator('html').getAttribute('lang') || '').startsWith(language) === false)
                    await page.getByRole('button',{name:language==='ar'?'AR':'EN',exact:true}).click();
                for (const [route,count] of [['/products',2],[`/products/${simple}/edit`,2],[`/products/${variable}/edit`,7],['/pos',4]]) {
                    await page.goto(`http://localhost:5173${route}`);
                    await page.waitForFunction(({selector,count})=>document.querySelectorAll(selector).length===count,{selector,count});
                    for (const image of await page.locator(selector).all()) {
                        await image.scrollIntoViewIfNeeded();
                        await image.evaluate(i=>i.decode());
                        if (await image.evaluate(i=>i.naturalWidth!==240 || i.getBoundingClientRect().width===0)) throw new Error('Image not rendered');
                    }
                    const actual=await page.locator(selector).evaluateAll(imgs=>imgs.map(i=>({src:i.currentSrc,width:i.naturalWidth,height:i.naturalHeight})));
                    const direction=await page.locator('html').getAttribute('dir');
                    if (direction!==(language==='ar'?'rtl':'ltr')) throw new Error('Direction mismatch');
                    evidence.push({route,language,direction,viewport,actual});
                    if (route==='/pos') await page.screenshot({path:`.artifacts/media-cert-pos-${language}-${viewport.width}.png`});
                }
            }
        }
        if (failures.length) throw new Error(JSON.stringify(failures));
        return {checkpoints:evidence.length,failures,evidence};
    } catch (error) {
        await page.screenshot({path:'.artifacts/media-cert-admin-sweep-failure.png'});
        throw error;
    } finally {
        page.off('response',listener);
    }
}

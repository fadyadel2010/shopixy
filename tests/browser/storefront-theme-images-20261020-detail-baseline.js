async page => {
    const results = [];
    const storefront = await page.context().newPage();
    try {
        for (const theme of ['modern','minimal','fashion','pearl','phoenix','hype','alizon','sharp','cosmetics','seven']) {
            const preview = await page.evaluate(async id => {
                const { storeConfigService } = await import('/src/modules/storefront/services/storeConfigService.js');
                return storeConfigService.createThemePreviewLink(id);
            }, theme);
            const url = await page.evaluate(href => { const u = new URL(href); u.pathname = '/products/iphone-17-pro-max'; return u.toString(); }, preview.previewUrl);
            await storefront.goto(url);
            await storefront.locator('[data-purchase-experience] img').first().waitFor({ timeout:30000 });
            for (const [width,height] of [[1440,1000],[390,844]]) {
                await storefront.setViewportSize({width,height});
                await storefront.screenshot({path:`.ai/evidence/storefront-theme-images-20261020/before-dark-${theme}-${width}-detail.png`});
                results.push(await storefront.evaluate(({theme,width,height}) => ({theme,width,height,
                    images:[...document.querySelectorAll('[data-purchase-experience] img')].map(img => {
                        const b=img.getBoundingClientRect(), p=img.parentElement.getBoundingClientRect(),s=getComputedStyle(img);
                        return {src:img.src,width:b.width,height:b.height,parentWidth:p.width,parentHeight:p.height,
                            naturalWidth:img.naturalWidth,naturalHeight:img.naturalHeight,fit:s.objectFit,position:s.position};
                    }),
                    titleColor:getComputedStyle(document.querySelector('main h1')).color,
                    overflow:document.documentElement.scrollWidth>width,
                }),{theme,width,height}));
            }
        }
    } finally {await storefront.close();}
    return results;
}

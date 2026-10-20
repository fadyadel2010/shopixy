async page => {
    const root = '.ai/evidence/storefront-theme-images-20261020';
    const phase = 'before';
    const palette = await page.locator('#appearance-background').inputValue();
    const paletteName = palette.toLowerCase() === '#0f172a' ? 'dark-minimal' : 'default';
    const themes = await page.evaluate(async () => {
        const { getAvailableMarketplaceThemes } = await import('/src/modules/storefront/themes/themeMarketplaceRegistry.js');
        return getAvailableMarketplaceThemes().map(theme => theme.id);
    });
    const results = [];
    const storefront = await page.context().newPage();
    await storefront.emulateMedia({ reducedMotion: 'reduce' });
    try {
        for (const id of themes) {
            const preview = await page.evaluate(async themeId => {
                const { storeConfigService } = await import('/src/modules/storefront/services/storeConfigService.js');
                return storeConfigService.createThemePreviewLink(themeId);
            }, id);
            await storefront.goto(preview.previewUrl);
            await storefront.locator('header').first().waitFor({ timeout: 30000 });
            for (const [width, height] of [[1440,1000], [390,844]]) {
                await storefront.setViewportSize({ width, height });
                await storefront.waitForFunction(() => document.querySelector('main') && document.querySelectorAll('h1').length > 0);
                await storefront.evaluate(async () => {
                    window.scrollTo(0,document.documentElement.scrollHeight);
                    await Promise.all([...document.images].filter(i => i.loading !== 'lazy').map(i => i.decode().catch(() => {})));
                });
                await storefront.evaluate(() => window.scrollTo(0,0));
                await storefront.screenshot({ path: `${root}/${phase}-${paletteName}-${id}-${width}-home.png`, fullPage: true });
                results.push(await storefront.evaluate(({ id, width, height }) => {
                    const css = el => {
                        const s = getComputedStyle(el), b = el.getBoundingClientRect();
                        return { tag: el.tagName, text: el.textContent?.trim().slice(0,80), class: el.className,
                            color:s.color, background:s.backgroundColor, border:s.borderColor, width:b.width, height:b.height,
                            aspectRatio:s.aspectRatio, objectFit:s.objectFit, position:s.position };
                    };
                    const rootStyle = getComputedStyle(document.documentElement);
                    return { id, width, height, overflow: document.documentElement.scrollWidth > width,
                        tokens: Object.fromEntries(['primary','secondary','background','text','muted'].map(key => [key,rootStyle.getPropertyValue(`--color-${key}`).trim()])),
                        surfaces: [...document.querySelectorAll('header, footer, h1, h2, [data-product-card-theme]')].slice(0,25).map(css),
                        images: [...document.images].map(img => ({...css(img), naturalWidth:img.naturalWidth, naturalHeight:img.naturalHeight,
                            complete:img.complete, src:img.src.split('?')[0], parent:css(img.parentElement) })),
                        productLinks: [...document.querySelectorAll('a[href*="/products/"]')].map(a => new URL(a.href).pathname).filter((v,i,a) => a.indexOf(v) === i) };
                }, {id,width,height}));
            }
        }
    } finally { await storefront.close(); }
    return { phase, paletteName, results };
}

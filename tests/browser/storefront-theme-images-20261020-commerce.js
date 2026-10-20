async page => {
    const root='.ai/evidence/storefront-theme-images-20261020', results=[], failures=[];
    const sf=await page.context().newPage();
    let originalQuantity=0, originalItems=[], changed=false;
    const productId='15e044bc-eaa1-c905-31a4-658aac4ad14b', variantId='a9705901-79f8-4bb6-a8a0-9d26b1c2ceaa';
    const cart=()=>sf.evaluate(async()=>{const {useCartStore}=await import('/src/store/cartStore.ts');await useCartStore.getState().fetchCart();return useCartStore.getState().cart;});
    try{
        await sf.setViewportSize({width:1440,height:1000});
        await sf.goto('http://ahramstore.localhost:5174/products/cert-media-simple-20261007');
        await sf.locator('main h1').waitFor();
        const before=await cart();originalItems=before?.items||[];
        originalQuantity=originalItems.find(i=>i.variantId===variantId)?.quantity||0;
        results.push({stage:'original-cart',items:originalItems.map(i=>({productId:i.productId,variantId:i.variantId,quantity:i.quantity})),currency:before?.currency});
        const add=sf.locator('section[data-purchase-experience]').first().getByRole('button',{name:/Add to Cart|إضافة إلى السلة/i}).first();
        if(await add.isDisabled())throw new Error('Representative product cannot currently be added; no data was forced.');
        await add.click();changed=true;
        await sf.waitForFunction(async({variantId,quantity})=>{const {useCartStore}=await import('/src/store/cartStore.ts');return useCartStore.getState().cart?.items.some(i=>i.variantId===variantId&&i.quantity===quantity);},{variantId,quantity:originalQuantity+1});
        await sf.goto('http://ahramstore.localhost:5174/cart');await sf.getByRole('button',{name:'Remove from cart'}).first().waitFor();
        results.push({stage:'cart-added',items:(await cart()).items.map(i=>({productId:i.productId,variantId:i.variantId,quantity:i.quantity}))});
        results.push({stage:'cart-image-styles',images:await sf.locator('main img').evaluateAll(ns=>ns.map(i=>({width:i.width,height:i.height,fit:getComputedStyle(i).objectFit}))) });
        await sf.screenshot({path:`${root}/after-cart-nonempty.png`});
        await sf.goto('http://ahramstore.localhost:5174/checkout');
        await sf.locator('main input, main select').first().waitFor({timeout:30000});
        results.push({stage:'checkout',path:await sf.evaluate(()=>location.pathname),forms:await sf.locator('input,select,textarea').evaluateAll(ns=>ns.map(n=>({id:n.id,type:n.type,color:getComputedStyle(n).color,background:getComputedStyle(n).backgroundColor}))),body:(await sf.locator('main').innerText()).slice(0,1500)});
        await sf.screenshot({path:`${root}/after-checkout-nonempty.png`});
        await sf.setViewportSize({width:390,height:844});
        results.push({stage:'checkout-mobile',overflow:await sf.evaluate(()=>document.documentElement.scrollWidth>document.documentElement.clientWidth+1)});
        await sf.screenshot({path:`${root}/after-checkout-nonempty-mobile.png`});
        // No order submission/payment/stock mutation is authorized by this visual check.
        await sf.goto('http://ahramstore.localhost:5174/products/cert-media-variable-20261007');await sf.locator('.variant-group').first().waitFor();
        const selections=[];
        for(const group of await sf.locator('.variant-group').all()){
            const button=group.locator('button:not(:disabled)').first();await button.click();selections.push(await button.textContent());
        }
        results.push({stage:'variant-selection',selections,pressed:await sf.locator('.variant-option[aria-pressed="true"]').count(),image:await sf.locator('.sf-media--product-detail img').getAttribute('src')});
        await sf.screenshot({path:`${root}/after-variant-selection.png`});
        await sf.keyboard.press('Control+k');
        const search=sf.getByRole('dialog');
        if(await search.count()){
            await search.locator('input').first().fill('CERT');
            await search.locator('a[href*="/products/"]').first().waitFor({timeout:15000});
            await sf.screenshot({path:`${root}/after-search.png`});
            results.push({stage:'search',dialog:true,images:await search.locator('img').evaluateAll(ns=>ns.map(i=>({width:i.width,height:i.height,fit:getComputedStyle(i).objectFit})))});await sf.keyboard.press('Escape');
        }else results.push({stage:'search',dialog:false});
    }catch(error){failures.push({reason:error.message});}
    finally{
        if(changed){
            const cleanup=await sf.evaluate(async({productId,variantId,originalQuantity})=>{const {useCartStore}=await import('/src/store/cartStore.ts');
                const s=useCartStore.getState();return originalQuantity? s.updateQuantity(productId,originalQuantity,variantId):s.removeItem(productId,variantId);},{productId,variantId,originalQuantity});
            results.push({stage:'cart-cleanup',accepted:cleanup});
        }
        const final=await cart();results.push({stage:'final-cart',items:(final?.items||[]).map(i=>({productId:i.productId,variantId:i.variantId,quantity:i.quantity}))});
        await sf.close();
    }
    return {results,failures,passed:failures.length===0};
}

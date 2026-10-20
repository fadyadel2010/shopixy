async page => {
    const storefront=page.context().pages().find(p=>p.url().startsWith('http://ahramstore.localhost:5174/'));
    const cart=await storefront.evaluate(async()=>(await(await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/cart')).data);
    if(cart.items.length)return{failed:'Existing cart nonempty; inspect before mutation',cart};
    await storefront.goto('http://ahramstore.localhost:5174/products/cert-media-simple-20261007');
    const add=storefront.waitForResponse(r=>r.url().includes('/cart/items')&&r.request().method()==='POST');
    await storefront.getByRole('region',{name:'Superstore buying confidence'}).getByRole('button',{name:'Add to Cart',exact:true}).click();
    if((await add).status()!==200)throw new Error('Add failed');
    await storefront.goto('http://ahramstore.localhost:5174/cart');
    await storefront.getByRole('button',{name:'Proceed to Checkout',exact:true}).click();
    for(const[name,value]of Object.entries({'Full Name':'MWNOSB20261020 COD TEST ONLY','Phone Number':'1000000097','Email Address':'wallet-no-sandbox-qa@example.test','City':'QA City','Area / District':'QA Area','Street Address':'TEST ONLY - NO DELIVERY','Order Notes':'MWNOSB20261020-COD TEST ONLY no payment no shipment'}))await storefront.getByRole('textbox',{name,exact:true}).fill(value);
    await storefront.getByRole('radio',{name:/^Standard Shipping/}).check();
    await storefront.getByRole('button',{name:'Continue to Payment',exact:true}).click();
    const methods=await storefront.locator('input[name=paymentMethod]').evaluateAll(inputs=>inputs.map(i=>i.value));
    if(methods.join(',')!=='COD')throw new Error('Wallet must be disabled before COD checkout');
    await storefront.getByText('Cash on Delivery',{exact:true}).click();
    await storefront.getByRole('button',{name:'Review Order',exact:true}).click();
    await storefront.waitForFunction(()=>[...document.querySelectorAll('button')].some(b=>b.textContent.includes('Place Order')&&!b.disabled));
    await storefront.evaluate(async()=>{
        const loaded=performance.getEntriesByType('resource').find(e=>e.name.includes('/src/services/api/httpClient.ts')).name;
        const{httpClient}=await import(loaded);
        httpClient.interceptors.response.use(r=>{
            if(/\/api\/storefront\/checkout\/?$/.test((r.config.url||'').split('?')[0])){
                const body={...r.data};for(const key of Object.keys(body))if(/secret|token/i.test(key))delete body[key];
                sessionStorage.setItem('mwnosb-cod-result',JSON.stringify({status:r.status,body,request:JSON.parse(r.config.data||'{}')}));
            }return r;
        });
    });
    await storefront.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/cod-review-wallet-disabled.png',fullPage:true});
    const response=storefront.waitForResponse(r=>/\/api\/storefront\/checkout\/?(?:\?|$)/.test(r.url())&&r.request().method()==='POST',{timeout:60000});
    await storefront.getByRole('button',{name:'Place Order',exact:true}).click();
    const r=await response;
    if(r.status()===200)await storefront.waitForURL(url=>/\/checkout\/(pending|success)/.test(url.pathname),{timeout:45000});
    const result=await storefront.evaluate(()=>JSON.parse(sessionStorage.getItem('mwnosb-cod-result')||'null'));
    await storefront.screenshot({path:'.ai/evidence/manual-wallet-no-sandbox-20261020/cod-completed.png',fullPage:true});
    return{methods,result,status:r.status(),passed:r.status()===200&&result.body.totalAmount===35&&result.body.paymentMethodCode==='COD',inspectPersistedStateBeforeRetry:true};
}

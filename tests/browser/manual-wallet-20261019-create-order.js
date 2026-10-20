async page => {
    const kind=await page.evaluate(()=>sessionStorage.getItem('mwqa-next-kind')||'REJECT');
    const marker='MWQA20261019-'+kind;
    const checks=[],network=[];
    const check=(name,pass)=>{checks.push({name,passed:!!pass});if(!pass)throw Error(name);};
    let committed;
    const listener=async r=>{
        if(/\/api\/storefront\/checkout\/?(?:\?|$)/.test(r.url())&&r.request().method()==='POST') {
            try {
                const data=await r.json();network.push({url:r.url().split('?')[0],status:r.status(),data});
                if(data.orderId) committed=data;
            } catch { network.push({url:r.url().split('?')[0],status:r.status(),bodyUnavailableAfterNavigation:true}); }
        }
    };
    page.on('response',listener);
    try{
        const cart=await page.evaluate(async()=> (await (await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/cart')).data);
        check('Preserve prior cart; only start when empty',cart.items.length===0);
        await page.goto('http://ahramstore.localhost:5174/products/cert-media-simple-20261007');
        await page.locator('main').getByRole('button',{name:'Add to Cart',exact:true}).click();
        await page.goto('http://ahramstore.localhost:5174/checkout');
        await page.getByRole('textbox',{name:'Full Name',exact:true}).fill(marker);
        await page.getByRole('textbox',{name:'Phone Number',exact:true}).fill('1000000099');
        await page.getByRole('textbox',{name:'Email Address',exact:true}).fill('manual-wallet-qa@example.test');
        await page.getByRole('textbox',{name:'City',exact:true}).fill('QA City');
        await page.getByRole('textbox',{name:'Area / District',exact:true}).fill('QA Area');
        await page.getByRole('textbox',{name:'Street Address',exact:true}).fill('TEST ONLY - no real delivery');
        await page.getByRole('textbox',{name:'Order Notes',exact:true}).fill(marker+' TEST ONLY no transfer or shipment');
        await page.getByRole('radio',{name:/^Standard Shipping/}).check();
        await page.getByRole('button',{name:'Continue to Payment',exact:true}).click();
        if(kind==='COD') {
            await page.getByText('Cash on Delivery',{exact:true}).click();
        } else {
            await page.getByText('P5QA20261017-817 TEST ONLY Wallet',{exact:true}).click();
            await page.getByRole('button',{name:'VF Vodafone Cash',exact:true}).click();
            await page.getByPlaceholder('01xxxxxxxxx').fill('01000000099');
            await page.locator('input[type=file]').setInputFiles('.ai/evidence/manual-wallet-20261019/TEST-ONLY-NOT-A-REAL-RECEIPT.png');
        }
        await page.getByRole('button',{name:'Review Order',exact:true}).click();
        const button=page.getByRole('button',{name:'Place Order',exact:true});
        await button.waitFor();
        await page.waitForFunction(()=> Array.from(document.querySelectorAll('button')).some(b=>b.textContent.includes('Place Order')&&!b.disabled));
        check('No coupon used and authoritative total displayed as EGP35',await page.locator('main').innerText().then(t=>t.includes('35.00')&&!t.includes('Remove coupon')));
        await page.screenshot({path:'.ai/evidence/manual-wallet-20261019/'+kind.toLowerCase()+'-review.png',fullPage:true});
        await button.click();
        await page.waitForURL(u=>u.pathname.includes('/checkout/pending')||u.pathname.includes('/checkout/success'),{timeout:45000});
        const orderId=await page.evaluate(()=>new URL(location.href).searchParams.get('orderId'));
        check('Real browser checkout persisted order identity',!!orderId);
        const outcome={marker,orderId,url:page.url(),committed,network,checks};
        await page.evaluate(o=>sessionStorage.setItem('mwqa-'+o.marker,JSON.stringify(o)),outcome);
        return outcome;
    }catch(e){return {marker,checks,network,committed,url:page.url(),failure:e.message,text:await page.locator('main').innerText()};}
    finally{page.off('response',listener);}
}

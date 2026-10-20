async page => {
    const kind = await page.evaluate(() => sessionStorage.getItem('ccqa-kind') || 'COD-BEFORE');
    const marker = 'CCQA20261019-' + kind;
    const network = [], consoleErrors = [];
    const listener = async r => {
        if (/\/api\/storefront\/checkout\/(pricing)(?:\?|$)/.test(r.url())) {
            try { network.push({status:r.status(),path:r.url().split('?')[0],body:await r.json()}); } catch { /* navigation */ }
        }
    };
    const errors = e => consoleErrors.push(e.message);
    page.on('response',listener); page.on('pageerror',errors);
    try {
        await page.evaluate(async marker => {
            const {httpClient}=await import('/src/services/api/httpClient.ts');
            const capture=r=>{
                if (/\/api\/storefront\/checkout\/?$/.test((r.config.url||'').split('?')[0])) {
                    const body={...r.data};
                    for (const key of ['guestOrderAccessToken','guestOrderAccessRefreshToken','clientSecret','manualWalletUploadToken']) delete body[key];
                    sessionStorage.setItem(marker+'-http',JSON.stringify({status:r.status,body,request:JSON.parse(r.config.data||'{}')}));
                }
                return r;
            };
            httpClient.interceptors.response.use(capture,e=>{if(e.response)capture(e.response);return Promise.reject(e);});
        },marker);
        if (!(await page.getByRole('textbox',{name:'Full Name',exact:true}).isVisible())) {
            await page.getByRole('button',{name:'Edit',exact:true}).first().click();
        }
        await page.getByRole('textbox',{name:'Full Name',exact:true}).fill(marker);
        await page.getByRole('textbox',{name:'Phone Number',exact:true}).fill('1000000099');
        await page.getByRole('textbox',{name:'Email Address',exact:true}).fill('coupon-checkout-qa@example.test');
        await page.getByRole('textbox',{name:'City',exact:true}).fill('QA City');
        await page.getByRole('textbox',{name:'Area / District',exact:true}).fill('QA Area');
        await page.getByRole('textbox',{name:'Street Address',exact:true}).fill('TEST ONLY - no real delivery');
        await page.getByRole('textbox',{name:'Order Notes',exact:true}).fill(marker+' TEST ONLY no transfer or shipment');
        await page.getByRole('radio',{name:/^Express Shipping/}).check();
        await page.getByRole('radio',{name:/^Standard Shipping/}).check();
        await page.getByRole('button',{name:'Continue to Payment',exact:true}).click();
        if (kind.startsWith('WALLET')) {
            await page.getByText('P5QA20261017-817 TEST ONLY Wallet',{exact:true}).click();
            await page.getByRole('button',{name:'VF Vodafone Cash',exact:true}).click();
            await page.getByPlaceholder('01xxxxxxxxx').fill('01000000099');
            await page.locator('input[type=file]').setInputFiles('.ai/evidence/coupon-checkout-20261019/TEST-ONLY-NOT-A-REAL-RECEIPT.png');
        } else {
            await page.getByText('Cash on Delivery',{exact:true}).click();
        }
        await page.getByRole('button',{name:'Review Order',exact:true}).click();
        const button = page.getByRole('button',{name:'Place Order',exact:true});
        await button.waitFor({state:'visible'});
        await page.waitForFunction(()=>Array.from(document.querySelectorAll('button')).some(b=>b.textContent.includes('Place Order')&&!b.disabled));
        const text = await page.locator('main').innerText();
        await page.screenshot({path:'.ai/evidence/coupon-checkout-20261019/'+kind.toLowerCase()+'-review.png',fullPage:true});
        const response = page.waitForResponse(r=>/\/api\/storefront\/checkout\/?(?:\?|$)/.test(r.url())&&r.request().method()==='POST',{timeout:45000});
        await button.click();
        const r=await response;
        if(r.status()===200) await page.waitForURL(u=>u.pathname.includes('/checkout/pending')||u.pathname.includes('/checkout/success'),{timeout:45000});
        const captured=await page.evaluate(marker=>JSON.parse(sessionStorage.getItem(marker+'-http')||'null'),marker);
        if(!captured) throw Error('Missing actual HTTP capture; inspect persisted state before retry');
        const outcome={marker,...captured,reviewText:text,network,consoleErrors};
        await page.evaluate(o=>sessionStorage.setItem(o.marker,JSON.stringify(o)),outcome);
        return outcome;
    } catch(e) { return {marker,failure:e.message,network,consoleErrors,url:page.url(),text:await page.locator('main').innerText()}; }
    finally { page.off('response',listener); page.off('pageerror',errors); }
}

async page=>{
    const marker=await page.evaluate(async()=>(await import('/src/store/checkoutStore.ts')).useCheckoutStore.getState().address.fullName);
    if(!marker.startsWith('MWQA20261019-'))throw Error('Not current QA checkout');
    await page.getByRole('button',{name:'VF Vodafone Cash',exact:true}).click();
    await page.getByPlaceholder('01xxxxxxxxx').fill('01000000099');
    await page.locator('input[type=file]').setInputFiles('.ai/evidence/manual-wallet-20261019/TEST-ONLY-NOT-A-REAL-RECEIPT.png');
    await page.getByRole('button',{name:'Review Order',exact:true}).click();
    await page.waitForFunction(()=>Array.from(document.querySelectorAll('button')).some(b=>b.textContent.includes('Place Order')&&!b.disabled));
    const promise=page.waitForResponse(r=>/\/api\/storefront\/checkout\/?(?:\?|$)/.test(r.url())&&r.request().method()==='POST');
    await page.getByRole('button',{name:'Place Order',exact:true}).click();
    const r=await promise;
    return {marker,status:r.status(),data:await r.json(),request:r.request().postDataJSON()};
}

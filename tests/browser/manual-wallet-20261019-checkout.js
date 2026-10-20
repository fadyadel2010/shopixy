// Resume actual Storefront checkout at shipping. No coupons, real transfers or provider calls.
async page => {
    const marker = 'MWQA20261019-VALID';
    const evidence = {marker,checks:[]};
    const check = (name,pass,data={}) => { evidence.checks.push({name,passed:!!pass,...data}); if(!pass) throw new Error(name); };
    const proof = await page.context().newPage();
    await proof.setContent('<html><body style="font:32px sans-serif;padding:50px;border:10px solid red"><h1>TEST-ONLY — NOT A REAL RECEIPT</h1><p>MWQA20261019 synthetic Manual Wallet lifecycle verification</p><p>No wallet or bank transfer occurred.</p><p>Expected test amount: EGP 35.00</p></body></html>');
    await proof.screenshot({path:'.ai/evidence/manual-wallet-20261019/TEST-ONLY-NOT-A-REAL-RECEIPT.png'});
    await proof.close();
    try {
        await page.getByRole('textbox',{name:'Full Name',exact:true}).fill(marker);
        await page.getByRole('textbox',{name:'Phone Number',exact:true}).fill('1000000099');
        await page.getByRole('textbox',{name:'Email Address',exact:true}).fill('manual-wallet-qa@example.test');
        await page.getByRole('textbox',{name:'City',exact:true}).fill('QA City');
        await page.getByRole('textbox',{name:'Area / District',exact:true}).fill('QA Area');
        await page.getByRole('textbox',{name:'Street Address',exact:true}).fill('TEST ONLY - no real delivery');
        await page.getByRole('textbox',{name:'Order Notes',exact:true}).fill(marker+' TEST ONLY no transfer or shipment');
        await page.getByRole('radio',{name:/^Standard Shipping/}).check();
        await page.getByRole('button',{name:'Continue to Payment',exact:true}).click();
        check('Real shipping UI progresses to payment', await page.getByRole('heading',{name:'Payment Method',exact:true}).isVisible());
        return {...evidence,text:await page.locator('main').innerText()};
    } catch(e) { return {...evidence,failure:e.message}; }
}

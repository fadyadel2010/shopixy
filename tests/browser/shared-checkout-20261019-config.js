async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    await admin.goto('http://localhost:5173/ecommerce/payment-methods');
    const row = admin.getByRole('row').filter({hasText:'MANUALWALLET'});
    await row.getByRole('button',{name:'Enable',exact:true}).waitFor();
    const before = await admin.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/payment-methods/MANUALWALLET')).data);
    if (before.isEnabled || before.publicConfiguration?.walletNumber) throw Error('Unexpected original wallet: inspect before mutation');
    await row.getByRole('button',{name:'Configure',exact:true}).click();
    await admin.locator('#payment-config-MANUALWALLET-walletNumber').fill('01000000000');
    await admin.getByRole('button',{name:'Save',exact:true}).click();
    await admin.locator('#payment-config-MANUALWALLET-walletNumber').waitFor({state:'hidden'});
    await row.getByRole('button',{name:'Enable',exact:true}).click();
    await row.getByRole('button',{name:'Disable',exact:true}).waitFor();
    const after = await admin.evaluate(async () => (await (await import('/src/services/api.js')).default.get('/admin/payment-methods/MANUALWALLET')).data);
    const proof = await page.context().newPage();
    await proof.setContent('<html><body style="font:30px sans-serif;padding:45px;border:12px solid red"><h1>TEST-ONLY — NOT A REAL RECEIPT</h1><p>SHQA20261019 shared checkout test.</p><p>No wallet or bank transfer occurred. No delivery authorized.</p><p>Synthetic proof only, at the stated intent amount.</p></body></html>');
    await proof.screenshot({path:'.ai/evidence/shared-checkout-20261019/TEST-ONLY-NOT-A-REAL-RECEIPT.png'});
    await proof.close();
    return {before,after,mustRestoreDisabledEmpty:true};
}

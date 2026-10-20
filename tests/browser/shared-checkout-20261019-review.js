async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const target = await page.evaluate(() => JSON.parse(sessionStorage.getItem('shqa-review') || 'null'));
    if (!target?.orderId || !target?.paymentIntentId || !target.label?.startsWith('shqa-')) throw Error('Missing exact task review target');
    const read = () => admin.evaluate(async t => {
        const api = (await import('/src/services/api.js')).default;
        return {details:(await api.get('/admin/orders/'+t.orderId)).data,list:(await api.get('/admin/orders',{params:{search:t.orderNumber,pageSize:20}})).data};
    },target);
    await admin.goto('http://localhost:5173/ecommerce/orders');
    await admin.getByRole('heading',{name:'Orders',exact:true}).waitFor();
    await admin.getByPlaceholder('Order, customer, email, phone').fill(target.orderNumber);
    const before = await read();
    if (before.list.totalCount !== 0) throw Error('Unapproved wallet order incorrectly appears in ordinary Orders');
    await admin.screenshot({path:'.ai/evidence/shared-checkout-20261019/'+target.label+'-ordinary-before.png',fullPage:true});
    await admin.goto('http://localhost:5173/ecommerce/manual-payments');
    const row = admin.getByRole('row').filter({hasText:target.orderNumber});
    await row.waitFor();
    await admin.screenshot({path:'.ai/evidence/shared-checkout-20261019/'+target.label+'-pending.png',fullPage:true});
    await row.getByRole('button',{name:/proof/i}).click();
    await admin.getByRole('button',{name:'Close',exact:true}).waitFor();
    await admin.screenshot({path:'.ai/evidence/shared-checkout-20261019/'+target.label+'-proof.png',fullPage:true});
    await admin.getByRole('button',{name:'Close',exact:true}).click();
    await row.getByRole('button',{name:'Approve',exact:true}).click();
    const response = admin.waitForResponse(r => r.url().includes('/'+target.paymentIntentId+'/confirm') && r.request().method()==='POST');
    let concurrentDecision;
    if (target.concurrent) {
        const second = await admin.context().newPage();
        await second.goto('http://localhost:5173/ecommerce/manual-payments');
        await second.getByRole('row').filter({hasText:target.orderNumber}).getByRole('button',{name:'Approve',exact:true}).click();
        const other = second.waitForResponse(r => r.url().includes('/'+target.paymentIntentId+'/confirm') && r.request().method()==='POST');
        await Promise.all([admin.getByRole('button',{name:'Confirm',exact:true}).click(),second.getByRole('button',{name:'Confirm',exact:true}).click()]);
        const r2 = await other;
        concurrentDecision = {status:r2.status(),body:await r2.json()};
        await second.screenshot({path:'.ai/evidence/shared-checkout-20261019/'+target.label+'-concurrent.png',fullPage:true});
        await second.close();
    } else await admin.getByRole('button',{name:'Confirm',exact:true}).click();
    const r = await response;
    const decision = {status:r.status(),body:await r.json()};
    await admin.screenshot({path:'.ai/evidence/shared-checkout-20261019/'+target.label+'-decision.png',fullPage:true});
    const after = await read();
    if (target.underpayment) {
        if (decision.status !== 409 || !JSON.stringify(decision.body).includes('shortfall')) throw Error('Underpayment did not fail with its authoritative shortfall');
    } else if (![decision.status,concurrentDecision?.status].includes(200)) throw Error('No approval succeeded: inspect state before retry');
    if (!target.underpayment) {
        await admin.goto('http://localhost:5173/ecommerce/orders');
        await admin.getByPlaceholder('Order, customer, email, phone').fill(target.orderNumber);
        await admin.getByRole('row').filter({hasText:target.orderNumber}).waitFor();
        await admin.reload();
        await admin.getByRole('heading',{name:'Orders',exact:true}).waitFor();
        await admin.screenshot({path:'.ai/evidence/shared-checkout-20261019/'+target.label+'-ordinary-after.png',fullPage:true});
    }
    return {target,before,decision,concurrentDecision,after,text:await admin.locator('main').innerText()};
}

async page => {
    const a=page.context().pages()[0];
    const checks=[];
    const check=(name,pass,data={})=>{checks.push({name,passed:!!pass,...data});if(!pass)throw Error(name);};
    try {
        await a.goto('http://localhost:5173/ecommerce/orders');
        await a.getByRole('heading',{name:'Orders',exact:true}).waitFor();
        check('Pending order absent from ordinary Orders UI',!(await a.locator('main').innerText()).includes('ORD-C9C5-2026-01059'));
        await a.reload();
        await a.getByRole('heading',{name:'Orders',exact:true}).waitFor();
        check('Refresh does not promote pending order',!(await a.locator('main').innerText()).includes('ORD-C9C5-2026-01059'));
        await a.screenshot({path:'.ai/evidence/manual-wallet-20261019/orders-before-approval.png',fullPage:true});
        await a.goto('http://localhost:5173/ecommerce/manual-payments');
        const row=a.getByRole('row').filter({hasText:'ORD-C9C5-2026-01059'});
        await row.waitFor();
        await row.getByRole('button',{name:/proof/i}).click();
        await a.locator('img[alt]').filter({visible:true}).last().waitFor();
        await a.screenshot({path:'.ai/evidence/manual-wallet-20261019/proof-review.png',fullPage:true});
        await a.getByRole('button',{name:'Close',exact:true}).click();
        await row.getByRole('button',{name:'Approve',exact:true}).click();
        return {checks,confirmation:await a.locator('body').innerText()};
    }catch(e){return {checks,failure:e.message};}
}

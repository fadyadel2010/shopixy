async page => {
    const input=__REGISTRATION_PRIVATE_INPUT__;
    const context=await page.context().browser().newContext({viewport:{width:1440,height:1100}});
    const p=await context.newPage();
    const email='regphone20261020-valid-formatted-v2@example.test';
    await p.goto('http://ahramstore.localhost:5174/login?return=%2Faccount');
    await p.locator('#login-email').fill(email);
    await p.locator('#login-password').fill(input.existingPassword);
    const pending=p.waitForResponse(r=>r.url().includes('/api/storefront/auth/login')&&r.request().method()==='POST');
    await p.locator('form button[type="submit"]').click();
    const response=await pending;
    if(response.status()!==200) return {failed:true,loginStatus:response.status()};
    await p.getByRole('heading',{name:'My Account',exact:true}).waitFor();
    await p.getByText('+12025550102',{exact:true}).waitFor();
    const me=await p.evaluate(async()=>{const r=await fetch('/api/storefront/auth/me?lang=en',{credentials:'include'});return {status:r.status,body:await r.json()};});
    const phoneLabel=await p.getByText('Phone',{exact:true}).count();
    await p.screenshot({path:'.ai/evidence/registration-remove-phone-20261020/existing-phone-profile.png',fullPage:true});
    await p.getByRole('button',{name:'Logout',exact:true}).first().click();
    await p.waitForURL('http://ahramstore.localhost:5174/');
    await context.close();
    return {observedUtc:new Date().toISOString(),failed:me.status!==200||me.body.phone!=='+12025550102'||phoneLabel!==1,loginStatus:response.status(),phoneLabelVisible:phoneLabel===1,storedSyntheticPhoneVisible:true,me,loggedOut:true};
}

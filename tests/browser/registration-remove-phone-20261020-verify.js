async page => {
    const input = __REGISTRATION_PRIVATE_INPUT__;
    const out='.ai/evidence/registration-remove-phone-20261020/';
    const results=[];
    for(const kind of ['main','loss']) {
        const email='regphone20261020-remove-'+kind+'-'+input.runId+'@example.test';
        const url=input.verificationUrls[email.split('@')[0]];
        if(!url) throw Error('Missing locally captured verification email');
        const context=await page.context().browser().newContext({viewport:{width:1440,height:1100}});
        const p=await context.newPage();
        const verifiedPromise=p.waitForResponse(r=>r.url().includes('/api/storefront/auth/verify-email')&&r.request().method()==='POST');
        await p.goto(url);
        const verifiedResponse=await verifiedPromise;
        const verificationBody=await verifiedResponse.json();
        if(verifiedResponse.status()!==200||!verificationBody.success) return {failed:true,kind,verification:{status:verifiedResponse.status(),body:verificationBody}};
        await p.getByRole('heading',{name:'Email Verified!',exact:true}).waitFor();
        await p.screenshot({path:out+kind+'-email-verified.png',fullPage:true});
        await p.goto('http://ahramstore.localhost:5174/login?return=%2Faccount');
        await p.locator('#login-email').fill(email);
        await p.locator('#login-password').fill(input.password);
        const loginPromise=p.waitForResponse(r=>r.url().includes('/api/storefront/auth/login')&&r.request().method()==='POST');
        await p.locator('form button[type="submit"]').click();
        const loginResponse=await loginPromise;
        const loginBody=await loginResponse.json();
        if(loginResponse.status()!==200||loginBody.customer?.emailVerified!==true) return {failed:true,kind,loginStatus:loginResponse.status(),code:loginBody.code};
        await p.getByRole('heading',{name:'My Account',exact:true}).waitFor();
        await p.getByText(email,{exact:true}).waitFor();
        await p.reload();
        await p.getByRole('heading',{name:'My Account',exact:true}).waitFor();
        await p.getByText(email,{exact:true}).waitFor();
        const me=await p.evaluate(async()=>{const r=await fetch('/api/storefront/auth/me?lang=en',{credentials:'include'});return {status:r.status,body:await r.json()};});
        if(me.status!==200||!me.body.emailVerified||me.body.id!==loginBody.customer.id||(me.body.phone??null)!==null) throw Error('Verified account readback mismatch');
        await p.screenshot({path:out+kind+'-verified-account.png',fullPage:true});
        await p.getByRole('button',{name:'Logout',exact:true}).first().click();
        await p.waitForURL('http://ahramstore.localhost:5174/');
        results.push({kind,email,verification:{status:verifiedResponse.status(),success:true,correlation:verifiedResponse.headers()['x-correlation-id']},login:{status:loginResponse.status(),customerId:loginBody.customer.id,emailVerified:true},refreshed:me,loggedOut:true});
        await context.close();
    }
    return {observedUtc:new Date().toISOString(),localCaptureOnly:true,results};
}

async page => {
    const input = __REGISTRATION_PRIVATE_INPUT__;
    const context = await page.context().browser().newContext({viewport:{width:1440,height:1100}});
    const p = await context.newPage();
    await p.addInitScript(() => {window.name='remove-phone-20261020-loss';});
    await p.goto('http://ahramstore.localhost:5174/register');
    const email = 'regphone20261020-remove-loss-'+input.runId+'@example.test';
    await p.locator('#register-name').fill('QA Remove Phone Response Loss');
    await p.locator('#register-email').fill(email);
    await p.locator('#register-password').fill(input.password);
    await p.locator('#register-confirm-password').fill(input.password);
    let server;
    let count=0;
    await p.route('**/api/storefront/auth/register?*',async route => {
        count++;
        const response = await route.fetch({url:route.request().url().replace('ahramstore.localhost','localhost'),headers:{...route.request().headers(),host:'ahramstore.localhost:5174'}});
        const request=route.request().postDataJSON();
        server={status:response.status(),body:await response.json(),requestKeys:Object.keys(request),request:{displayName:request.displayName,email:request.email,password:'[REDACTED]'},correlation:response.headers()['x-correlation-id']};
        await route.abort('failed');
    });
    await p.locator('form button[type="submit"]').click();
    await p.getByRole('alert').waitFor();
    await p.unroute('**/api/storefront/auth/register?*');
    const preserved=(await p.locator('#register-email').inputValue())===email && (await p.locator('#register-name').inputValue())==='QA Remove Phone Response Loss' && (await p.locator('#register-password').inputValue())===input.password && (await p.locator('#register-confirm-password').inputValue())===input.password;
    await p.screenshot({path:'.ai/evidence/registration-remove-phone-20261020/response-loss.png',fullPage:true});
    return {observedUtc:new Date().toISOString(),failed:server.status!==200 || !preserved || count!==1,server,postCount:count,valuesPreserved:preserved,browserError:await p.getByRole('alert').innerText(),requiresSqlReadbackBeforeRetry:true};
}

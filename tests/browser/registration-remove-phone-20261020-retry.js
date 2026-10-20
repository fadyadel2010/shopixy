async page => {
    const input = __REGISTRATION_PRIVATE_INPUT__;
    let p;
    for (const context of page.context().browser().contexts()) {
        for (const candidate of context.pages()) {
            if(await candidate.evaluate(()=>window.name).catch(()=>'')==='remove-phone-20261020-loss') p=candidate;
        }
    }
    if(!p) throw Error('Missing task-owned recovery form');
    const responsePromise=p.waitForResponse(r=>r.url().includes('/api/storefront/auth/register')&&r.request().method()==='POST');
    await p.locator('form button[type="submit"]').click();
    const response=await responsePromise;
    const body=await response.json();
    await p.getByRole('alert').waitFor();
    const preserved=(await p.locator('#register-password').inputValue())===input.password && (await p.locator('#register-confirm-password').inputValue())===input.password && (await p.locator('#register-email').inputValue())==='regphone20261020-remove-loss-'+input.runId+'@example.test';
    await p.screenshot({path:'.ai/evidence/registration-remove-phone-20261020/duplicate-retry.png',fullPage:true});
    const result={observedUtc:new Date().toISOString(),failed:response.status()!==400||body.code!=='EMAIL_EXISTS'||!preserved,status:response.status(),body,valuesPreserved:preserved,requestKeys:Object.keys(response.request().postDataJSON()),browserError:await p.getByRole('alert').innerText()};
    await p.context().close();
    return result;
}

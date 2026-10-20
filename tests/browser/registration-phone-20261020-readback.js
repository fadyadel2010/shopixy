async page => {
  const input = __REGISTRATION_PRIVATE_INPUT__;
  const results = [];
  for (const [name, expectedPhone, expectedVerified] of [
    ['optional-untouched-final-v2', null, true],
    ['valid-formatted-v2', '+12025550102', true],
    ['response-loss', '+12025550105', false],
  ]) {
    const context = await page.context().browser().newContext();
    const p = await context.newPage();
    const email = 'regphone20261020-' + name + '@example.test';
    await p.goto('http://ahramstore.localhost:5174/login?return=%2Faccount');
    await p.locator('#login-email').fill(email);
    await p.locator('#login-password').fill(input.password);
    const pending = p.waitForResponse(r => r.url().includes('/api/storefront/auth/login') && r.request().method() === 'POST');
    await p.locator('form button[type="submit"]').click();
    const response = await pending;
    if (response.status() !== 200) throw new Error(name + ': login failed');
    await p.getByRole('heading', {name:'My Account',exact:true}).waitFor();
    await p.getByText(email, {exact:true}).waitFor();
    await p.reload();
    await p.getByRole('heading', {name:'My Account',exact:true}).waitFor();
    await p.getByText(email, {exact:true}).waitFor();
    if (expectedPhone) await p.getByText(expectedPhone, {exact:true}).waitFor();
    const me = await p.evaluate(async () => {
      const response = await fetch('/api/storefront/auth/me?lang=en', {credentials:'include'});
      return {status:response.status,body:await response.json()};
    });
    if (me.status !== 200 || (me.body.phone ?? null) !== expectedPhone || me.body.emailVerified !== expectedVerified) throw new Error(name + ': persistent profile mismatch');
    await p.screenshot({path:'.ai/evidence/registration-phone-20261020/' + name + '-persisted-account-final.png'});
    results.push({name,loginStatus:response.status(),refreshedAccountVisible:true,emailVisible:true,phoneVisible:expectedPhone !== null,me});
    await p.getByRole('button', {name:'Logout',exact:true}).first().click();
    await p.waitForURL('http://ahramstore.localhost:5174/');
    await context.close();
  }
  return {observedUtc:new Date().toISOString(),results};
}

async page => {
  const input = __REGISTRATION_PRIVATE_INPUT__;
  const results = [];
  for (const baseName of ['optional-untouched-final', 'valid-formatted']) {
    const name = baseName + (input.suffix || '');
    const context = await page.context().browser().newContext();
    const p = await context.newPage();
    const email = 'regphone20261020-' + name + '@example.test';
    const login = async () => {
      await p.goto('http://ahramstore.localhost:5174/login?return=%2Faccount');
      await p.locator('#login-email').fill(email);
      await p.locator('#login-password').fill(input.password);
      const pending = p.waitForResponse(r => r.url().includes('/api/storefront/auth/login') && r.request().method() === 'POST');
      await p.locator('form button[type="submit"]').click();
      const response = await pending;
      const body = await response.json();
      if (response.status() !== 200) throw new Error(name + ': login failed ' + response.status());
      await p.waitForURL('**/account');
      return {status:response.status(),customerId:body.customer.id,emailVerified:body.customer.emailVerified,phone:body.customer.phone ?? null,correlation:response.headers()['x-correlation-id']};
    };
    const me = () => p.evaluate(async () => {
      const response = await fetch('/api/storefront/auth/me?lang=en', {credentials:'include'});
      return {status:response.status, body:await response.json()};
    });
    const beforeLogin = await login();
    if (beforeLogin.emailVerified) throw new Error(name + ': unexpectedly preverified');
    await p.screenshot({path:'.ai/evidence/registration-phone-20261020/' + name + '-unverified-account.png'});
    if (!input.verificationUrls[name]) throw new Error(name + ': missing locally captured verification email');
    const pending = p.waitForResponse(r => r.url().includes('/api/storefront/auth/verify-email') && r.request().method() === 'POST');
    await p.goto(input.verificationUrls[name]);
    const response = await pending;
    const body = await response.json();
    if (response.status() !== 200 || !body.success) {
      await p.screenshot({path:'.ai/evidence/registration-phone-20261020/' + name + '-verification-failed.png'});
      return {failed:true,results:[...results,{name,beforeLogin,verification:{status:response.status(),body,correlation:response.headers()['x-correlation-id']}}]};
    }
    await p.getByRole('heading', {name:'Email Verified!'}).waitFor();
    const verified = await me();
    if (!verified.body.emailVerified) throw new Error(name + ': verification failed');
    await p.screenshot({path:'.ai/evidence/registration-phone-20261020/' + name + '-verified.png'});
    await p.getByRole('button', {name:'Logout', exact:true}).first().click();
    await p.waitForURL('http://ahramstore.localhost:5174/');
    const afterLogin = await login();
    if (!afterLogin.emailVerified || beforeLogin.customerId !== afterLogin.customerId) throw new Error(name + ': identity changed after verification');
    await p.reload();
    const refreshed = await me();
    if (refreshed.body.id !== beforeLogin.customerId || !refreshed.body.emailVerified) throw new Error(name + ': refresh lost verified identity');
    await p.screenshot({path:'.ai/evidence/registration-phone-20261020/' + name + '-verified-login-refresh.png'});
    results.push({name,beforeLogin,verification:{status:response.status(),success:body.success,correlation:response.headers()['x-correlation-id']},afterLogin,refreshed});
    await p.getByRole('button', {name:'Logout', exact:true}).first().click();
    await p.waitForURL('http://ahramstore.localhost:5174/');
    await context.close();
  }
  return {observedUtc:new Date().toISOString(),realSmtpCapturedLocally:true,realVerification:true,results};
}

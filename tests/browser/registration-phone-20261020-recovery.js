async page => {
  const input = __REGISTRATION_PRIVATE_INPUT__;
  let p;
  for (const context of page.context().browser().contexts()) {
    for (const candidate of context.pages()) {
      if (await candidate.evaluate(() => window.name).catch(() => '') === 'regphone20261020-recovery') p = candidate;
    }
  }
  if (!p) throw new Error('Task-owned recovery form not found');
  const pending = p.waitForResponse(r => r.url().includes('/api/storefront/auth/register') && r.request().method() === 'POST');
  await p.locator('form button[type="submit"]').click();
  const response = await pending;
  const body = await response.json();
  if (response.status() !== 400 || body.code !== 'EMAIL_EXISTS') throw new Error('Expected controlled existing-identity recovery');
  await p.getByRole('alert').waitFor();
  await p.screenshot({path:'.ai/evidence/registration-phone-20261020/response-loss-retry.png'});
  await p.getByRole('button', {name:'Sign in',exact:true}).click();
  await p.locator('#login-email').fill('regphone20261020-response-loss@example.test');
  await p.locator('#login-password').fill(input.password);
  const loginPending = p.waitForResponse(r => r.url().includes('/api/storefront/auth/login') && r.request().method() === 'POST');
  await p.locator('form button[type="submit"]').click();
  const loginResponse = await loginPending;
  const loginBody = await loginResponse.json();
  if (loginResponse.status() !== 200) throw new Error('Cannot log into committed identity');
  await p.waitForURL('http://ahramstore.localhost:5174/');
  await p.goto('http://ahramstore.localhost:5174/account');
  await p.screenshot({path:'.ai/evidence/registration-phone-20261020/response-loss-recovered-account.png'});
  const result = {observedUtc:new Date().toISOString(),retry:{status:response.status(),body,correlation:response.headers()['x-correlation-id']},login:{status:loginResponse.status(),customerId:loginBody.customer.id,emailVerified:loginBody.customer.emailVerified,phone:loginBody.customer.phone}};
  await p.getByRole('button', {name:'Logout',exact:true}).first().click();
  await p.waitForURL('http://ahramstore.localhost:5174/');
  await p.context().close();
  return result;
}

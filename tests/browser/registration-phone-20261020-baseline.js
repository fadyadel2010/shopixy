async page => {
  // Separate context preserves the supplied existing Storefront/Admin sessions.
  const context = await page.context().browser().newContext();
  const p = await context.newPage();
  await p.goto('http://ahramstore.localhost:5174/register');
  await p.locator('#register-name').waitFor();
  const results = [];
  for (const [name, phone] of [['baseline-none', undefined], ['baseline-valid', '+1 (202) 555-0101'], ['baseline-invalid', '12345']]) {
    await p.goto('http://ahramstore.localhost:5174/register');
    await p.locator('#register-name').fill('QA Optional Phone ' + name);
    await p.locator('#register-email').fill('regphone20261020-' + name + '@example.test');
    if (phone !== undefined) await p.locator('#register-phone').fill(phone);
    const password = await p.evaluate(() => 'Qa!' + crypto.randomUUID() + '1A');
    await p.locator('#register-password').fill(password);
    await p.locator('#register-confirm-password').fill(password);
    const pending = p.waitForResponse(r => r.url().includes('/api/storefront/auth/register') && r.request().method() === 'POST');
    await p.locator('form button[type="submit"]').click();
    const response = await pending;
    const body = await response.json();
    const payload = response.request().postDataJSON();
    delete payload.password;
    results.push({name, url:response.url(), payload, status:response.status(), body, correlation:response.headers()['x-correlation-id']});
    await p.screenshot({path:'.ai/evidence/registration-phone-20261020/' + name + '.png'});
  }
  await context.close();
  return {observedUtc:new Date().toISOString(), results};
}

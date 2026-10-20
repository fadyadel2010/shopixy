async page => {
  const input = __REGISTRATION_PRIVATE_INPUT__;
  const context = await page.context().browser().newContext();
  const p = await context.newPage();
  const results = [];
  const errors = [];
  p.on('pageerror', e => errors.push(e.name));
  const cases = [
    ['optional-untouched-final', undefined, 200, null],
    ['optional-cleared-final', '', 200, null],
    ['optional-whitespace-final', '   ', 200, null],
    ['valid-formatted', '+1 (202) 555-0102', 200, '+12025550102'],
    ['duplicate-exact', '+12025550102', 200, '+12025550102'],
    ['duplicate-equivalent', '+1 202-555-0102', 200, '+12025550102'],
    ['invalid-number', '12345', 400, null],
    ['invalid-format', 'not-a-phone', 400, null],
    ['repeat-email-only', undefined, 200, null],
    ['repeat-valid', '+12025550103', 200, '+12025550103'],
    ['refreshed-valid', '+1 (202) 555-0104', 200, '+12025550104'],
  ];
  for (const [baseName, phone, expected, canonical] of cases) {
    const name = baseName + (input.suffix || '');
    await p.goto('http://ahramstore.localhost:5174/register');
    if (baseName === 'refreshed-valid') await p.reload();
    await p.locator('#register-name').fill('QA Optional Phone ' + name);
    const email = 'regphone20261020-' + name + '@example.test';
    await p.locator('#register-email').fill(email);
    if (phone !== undefined) {
      if (name.startsWith('optional-cleared')) await p.locator('#register-phone').fill('+12025550104');
      await p.locator('#register-phone').fill(phone);
    }
    await p.locator('#register-password').fill(input.password);
    await p.locator('#register-confirm-password').fill(input.password);
    const captured = [];
    const listener = req => { if (req.method() === 'POST' && req.url().includes('/api/storefront/auth/register')) captured.push(req); };
    p.on('request', listener);
    const pending = p.waitForResponse(r => r.url().includes('/api/storefront/auth/register') && r.request().method() === 'POST');
    if (baseName === 'repeat-valid') {
      // Same event-loop turn: the ref guard must beat React's disabled render.
      await p.locator('form').evaluate(form => { form.requestSubmit(); form.requestSubmit(); });
    } else await p.locator('form button[type="submit"]').click();
    const response = await pending;
    const body = await response.json();
    const payload = response.request().postDataJSON(); delete payload.password;
    if (response.status() !== expected) return {failed:true,results:[...results,{name,payload,status:response.status(),body,correlation:response.headers()['x-correlation-id']}]};
    let confirmation = false;
    let valuesPreserved = null;
    let fieldError = null;
    if (expected === 200) {
      await p.getByRole('heading', {name:'Account created!'}).waitFor();
      confirmation = true;
      if ((body.customer.phone ?? null) !== canonical) throw new Error(name + ': wrong canonical phone');
      if (body.accessToken || body.refreshToken || body.customer.emailVerified) throw new Error(name + ': unexpected authentication/verification');
    } else {
      await p.locator('#register-phone-error').waitFor();
      fieldError = await p.locator('#register-phone-error').textContent();
      valuesPreserved = (await p.locator('#register-email').inputValue()) === email &&
        (await p.locator('#register-phone').inputValue()) === phone &&
        (await p.locator('#register-password').inputValue()) === input.password &&
        (await p.locator('#register-name').inputValue()) === 'QA Optional Phone ' + name;
      if (!valuesPreserved || body.code !== 'INVALID_PHONE' || body.data?.field !== 'phone') throw new Error(name + ': validation contract mismatch');
    }
    if (captured.length !== 1) throw new Error(name + ': duplicate POST');
    p.off('request', listener);
    await p.screenshot({path:'.ai/evidence/registration-phone-20261020/' + name + '.png'});
    results.push({name,payload,status:response.status(),body,correlation:response.headers()['x-correlation-id'],confirmation,valuesPreserved,fieldError,postCount:captured.length});
    // Respect the existing 3/minute registration security policy; do not relax it.
    if (baseName !== cases[cases.length - 1][0]) await p.waitForTimeout(22000);
  }
  await context.close();
  return {observedUtc:new Date().toISOString(),headed:true,realBackend:true,results,pageErrors:errors};
}

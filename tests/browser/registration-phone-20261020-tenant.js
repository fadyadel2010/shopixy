async page => {
  const input = __REGISTRATION_PRIVATE_INPUT__;
  const context = await page.context().browser().newContext();
  const p = await context.newPage();
  await p.goto('http://ahramstore.localhost:5174/register');
  await p.locator('#register-name').fill('QA Optional Phone Tenant Spoof');
  await p.locator('#register-email').fill('regphone20261020-tenant-spoof@example.test');
  await p.locator('#register-phone').fill('+12025550102');
  await p.locator('#register-password').fill(input.password);
  await p.locator('#register-confirm-password').fill(input.password);
  // Real UI request and backend; negative request-header probe, no response mock.
  await p.route('**/api/storefront/auth/register?*', route => route.continue({
    headers:{...route.request().headers(),'x-tenant-id':'00000000-0000-0000-0000-000000000001'},
  }));
  const pending = p.waitForResponse(r => r.url().includes('/api/storefront/auth/register') && r.request().method() === 'POST');
  await p.locator('form button[type="submit"]').click();
  const response = await pending;
  const body = await response.json();
  if (response.status() !== 403) throw new Error('Foreign tenant header was not rejected');
  await p.getByRole('alert').waitFor();
  await p.screenshot({path:'.ai/evidence/registration-phone-20261020/tenant-spoof-rejected.png'});
  const payload = response.request().postDataJSON(); delete payload.password;
  const result = {observedUtc:new Date().toISOString(),status:response.status(),body,payload,correlation:response.headers()['x-correlation-id']};
  await context.close();
  return result;
}

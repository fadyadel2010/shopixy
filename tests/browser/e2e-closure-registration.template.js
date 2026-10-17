async page => {
  // Materialized only into approved private temporary storage. Never retain the
  // substituted input or credentials under the repository/evidence directory.
  const input = __PHASE5_PRIVATE_INPUT__;
  // Preserve the user's already-authenticated Storefront session unchanged.
  const context = await page.context().browser().newContext();
  const p = await context.newPage();
  await p.addInitScript(() => { window.name = 'phase5-e2e'; });
  await p.goto('http://ahramstore.localhost:5174/register');
  await p.locator('#register-name').waitFor();
  await p.locator('#register-name').fill(input.name);
  await p.locator('#register-email').fill(input.email);
  await p.locator('#register-phone').fill(input.phone);
  await p.locator('#register-password').fill(input.password);
  await p.locator('#register-confirm-password').fill(input.password);
  const responsePromise = p.waitForResponse(r => r.url().includes('/api/storefront/auth/register') && r.request().method() === 'POST');
  await p.locator('form button[type="submit"]').click();
  const response = await responsePromise;
  const body = await response.json();
  return {observedUtc:new Date().toISOString(),layer:'real-hosted-registration-UI',http:response.status(),
    code:body.code,success:body.success,customerId:body.customer?.id,
    verificationEmailSent:body.verificationEmailSent,
    authenticated:body.isAuthenticated,
    confirmationVisible:await p.getByText(/check your email|verification email|تحقق من بريدك/i).count() > 0,
    requestId:body.traceId ?? response.headers()['x-correlation-id'] ?? null};
}

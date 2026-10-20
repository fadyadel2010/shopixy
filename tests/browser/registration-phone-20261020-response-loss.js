async page => {
  const input = __REGISTRATION_PRIVATE_INPUT__;
  // A previous harness-only DNS failure was independently checked in SQL before
  // rerunning. Close only its explicitly named task context, never user contexts.
  for (const previous of page.context().browser().contexts()) {
    if (previous === page.context()) continue;
    for (const candidate of previous.pages()) {
      if (await candidate.evaluate(() => window.name).catch(() => '') === 'regphone20261020-recovery') {
        await previous.close();
        break;
      }
    }
  }
  const context = await page.context().browser().newContext();
  const p = await context.newPage();
  await p.addInitScript(() => { window.name = 'regphone20261020-recovery'; });
  await p.goto('http://ahramstore.localhost:5174/register');
  const email = 'regphone20261020-response-loss@example.test';
  await p.locator('#register-name').fill('QA Optional Phone Response Loss');
  await p.locator('#register-email').fill(email);
  await p.locator('#register-phone').fill('+12025550105');
  await p.locator('#register-password').fill(input.password);
  await p.locator('#register-confirm-password').fill(input.password);
  let server;
  let requests = 0;
  await p.route('**/api/storefront/auth/register?*', async route => {
    requests++;
    // The real API commits and returns normally. Drop only browser delivery,
    // never mock a registration success or alter the application response.
    // Windows resolves *.localhost in Edge, but Node's HTTP client does not.
    // Connect to loopback while preserving the REAL storefront Host/tenant.
    const response = await route.fetch({
      url:route.request().url().replace('ahramstore.localhost', 'localhost'),
      headers:{...route.request().headers(),host:'ahramstore.localhost:5174'},
    });
    const body = await response.json();
    const payload = route.request().postDataJSON(); delete payload.password;
    server = {status:response.status(),body,payload,correlation:response.headers()['x-correlation-id']};
    await route.abort('failed');
  });
  await p.locator('form button[type="submit"]').click();
  await p.getByRole('alert').waitFor();
  await p.unroute('**/api/storefront/auth/register?*');
  const preserved = (await p.locator('#register-email').inputValue()) === email &&
    (await p.locator('#register-phone').inputValue()) === '+12025550105' &&
    (await p.locator('#register-password').inputValue()) === input.password;
  await p.screenshot({path:'.ai/evidence/registration-phone-20261020/response-loss.png'});
  if (!preserved || server?.status !== 200 || requests !== 1) throw new Error('Response loss preparation failed');
  // Leave this task-owned form/context intact. SQL must be inspected before retry.
  return {observedUtc:new Date().toISOString(),server,browserError:await p.getByRole('alert').innerText(),valuesPreserved:preserved,postCount:requests,awaitingDatabaseReadback:true};
}

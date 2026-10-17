async page => {
  const input = __PHASE5_PRIVATE_INPUT__;
  const context = page.context().browser().contexts().at(-1);
  const p = context.pages().at(-1);
  await p.goto('http://ahramstore.localhost:5174/login');
  await p.locator('#login-email').fill(input.email);
  await p.locator('#login-password').fill(input.password);
  const pending = p.waitForResponse(r=>r.url().includes('/api/storefront/auth/login') && r.request().method()==='POST');
  await p.locator('form button[type="submit"]').click();
  const response = await pending;
  const data = await response.json();
  return {observedUtc:new Date().toISOString(),http:response.status(),code:data.code,
    customerId:data.customer?.id,emailVerified:data.customer?.emailVerified,
    requestId:data.traceId ?? response.headers()['x-correlation-id'] ?? null};
}

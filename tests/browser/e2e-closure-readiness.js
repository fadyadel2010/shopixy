async page => {
  const pages = page.context().pages();
  const admin = pages.find(p => p.url().startsWith('http://localhost:5173/'));
  const storefront = pages.find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
  if (!admin || !storefront) throw new Error('Expected existing application tabs');
  const administration = await admin.evaluate(async () => {
    const read = async path => {
      const r = await fetch('/api/' + path, { credentials: 'include' });
      let data = null; try { data = await r.json(); } catch {}
      return { http: r.status, data };
    };
    const session = await read('auth/session');
    const user = session.data?.user ?? session.data;
    const accounts = await read('admin/whatsapp/accounts');
    const whatsapp = [];
    for (const a of accounts.data ?? []) {
      const status = await read(`admin/whatsapp/accounts/${a.id}/status`);
      whatsapp.push({ id: a.id, enabled: a.enabled, status: a.status, otp: a.useForOtpVerification,
        orderConfirmation: a.useForOrderConfirmation, statusRead: status });
    }
    const shipping = await read('admin/shipping/carrier-accounts');
    // Explicit admission: never return raw account configuration/credentials.
    const carrierRows = Array.isArray(shipping.data) ? shipping.data : shipping.data?.items ?? [];
    const carriers = carrierRows.map(a => ({id:a.id,carrierCode:a.carrierCode,environment:a.environment,
      status:a.status,readiness:a.readiness,isEnabled:a.isEnabled,isExecutable:a.isExecutable,
      blockers:a.blockers,services:a.services?.map(s=>({serviceCode:s.serviceCode,enabled:s.enabled}))}));
    return { sessionHttp: session.http, tenantId: user?.tenantId, role: user?.role,
      whatsappHttp: accounts.http, whatsapp, shippingHttp:shipping.http, carriers };
  });
  // Status response can contain account phone metadata. Return whitelisted scalars only.
  for (const account of administration.whatsapp) {
    const d = account.statusRead.data;
    account.statusRead = {http:account.statusRead.http,status:d?.status,isConnected:d?.isConnected,
      circuitBreakerOpen:d?.circuitBreakerOpen,success:d?.success};
  }
  const customer = await storefront.evaluate(async () => {
    const response = await fetch('/api/storefront/auth/me', {credentials:'include'});
    let data; try { data=await response.json(); } catch {}
    return {http:response.status,authenticated:response.ok,verifiedEmail:data?.isEmailVerified,
      verifiedPhone:data?.isPhoneVerified};
  });
  return {observedUtc:new Date().toISOString(),administration,customer,mutations:0};
}

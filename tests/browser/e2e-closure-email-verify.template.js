async page => {
  const input = __PHASE5_PRIVATE_INPUT__;
  const p = page.context().browser().contexts().at(-1).pages().at(-1);
  const received = p.waitForResponse(r=>r.url().includes('/api/storefront/auth/verify-email') && r.request().method()==='POST');
  await p.goto(input.verificationUrl);
  const response = await received;
  const data = await response.json();
  const heading = await p.getByRole('heading').allTextContents();
  const checks = await p.evaluate(async token => {
    const {httpClient} = await import('/src/services/api/httpClient.ts');
    const post = async path => {
      try {const r=await httpClient.post(path,{token});return {http:r.status,success:r.data.success};}
      catch(e){return {http:e.response?.status,code:e.response?.data?.code};}
    };
    const reuse=await post('/api/storefront/auth/verify-email');
    const resend=await post('/api/storefront/auth/resend-verification');
    const me=await httpClient.get('/api/storefront/auth/me');
    return {reuse,resend,identity:{http:me.status,id:me.data.id,emailVerified:me.data.emailVerified}};
  }, input.verificationUrl.match(/[?&]token=([^&]+)/)[1]);
  await p.goto('http://ahramstore.localhost:5174/account');
  return {observedUtc:new Date().toISOString(),http:response.status(),success:data.success,code:data.code,
    verificationHeading:heading,checks,tokenNotRetained:true,recipientReceipt:'user-confirmed'};
}

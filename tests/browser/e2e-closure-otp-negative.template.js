async page => {
  const input=__PHASE5_PRIVATE_INPUT__;
  const context=await page.context().browser().newContext();
  const p=await context.newPage();
  try {
    await p.goto('http://ahramstore.localhost:5174/');
    await p.getByRole('heading',{name:'Welcome to Shopixy',exact:true}).waitFor();
    return await p.evaluate(async phone=>{
      const {httpClient}=await import('/src/services/api/httpClient.ts');
      const status=await httpClient.get('/api/storefront/checkout/phone-verification-status',{params:{phone}});
      let invalid;
      try {const r=await httpClient.post('/api/storefront/checkout/verify-phone-otp',{phone,otpCode:'invalid-nonnumeric'});invalid={http:r.status,success:r.data.success};}
      catch(e){invalid={http:e.response?.status,code:e.response?.data?.code};}
      const spoof=await fetch('/api/storefront/checkout/phone-verification-status?phone=invalid',{headers:{'X-Tenant-Id':'11111111-1111-4111-8111-111111111111'}});
      const spoofBody=await spoof.json();
      return {observedUtc:new Date().toISOString(),freshScope:{http:status.status,isRequired:status.data.isRequired,isVerified:status.data.isVerified},
        invalid,foreignTenant:{http:spoof.status,code:spoofBody.code},messagesDispatched:0};
    },input.phone);
  } finally {await context.close();}
}

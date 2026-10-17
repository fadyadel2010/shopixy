async page => {
  const input=__PHASE5_PRIVATE_INPUT__;
  const p=page.context().browser().contexts().at(-1).pages().at(-1);
  return await p.evaluate(async phone=>{
    const {httpClient}=await import('/src/services/api/httpClient.ts');
    const r=await httpClient.get('/api/storefront/checkout/phone-verification-status',{params:{phone}});
    return {observedUtc:new Date().toISOString(),http:r.status,isRequired:r.data.isRequired,isVerified:r.data.isVerified,
      verificationAuthority:'phone-specific-Redis-session/customer-scope',messagesDispatched:0};
  },input.phone);
}

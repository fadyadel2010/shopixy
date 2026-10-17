async page => page.evaluate(async () => {
  const api=(await import('/src/services/api.js')).default;
  const id='14030fd4-b1b5-4fed-bea5-80fb735c5f30';
  const accounts=(await api.get('/admin/shipping/carrier-accounts')).data;
  const other=accounts.find(a=>a.carrierCode!=='bosta');
  const negative=async operation=>{try{await operation();return{rejected:false};}catch(e){return{rejected:true,status:e.response?.status,code:e.response?.data?.code};}};
  const foreignCarrier=other?await negative(()=>api.get(`/admin/shipping/carrier-accounts/${other.id}/bosta/native-geography`)):null;
  const anonymous=await fetch(`/api/admin/shipping/carrier-accounts/${id}/bosta/native-geography`,{credentials:'omit',cache:'no-store',headers:{'X-Tenant-Id':'c9c5c1ae-949e-498e-bc63-07242daeb49d'}});
  let anonymousCode=null;try{const body=await anonymous.json();anonymousCode=body.code||body.error?.code||body.errorCode||null;}catch{}
  // Existing draft remains a separate unpublished operational artifact.
  const draft=(await api.get(`/admin/shipping/carrier-accounts/${id}/bosta/geography/sync/e9d38578-2e31-458f-9df6-8ba600917f42`)).data;
  const read=(await api.get('/admin/shipping/carrier-accounts')).data.find(a=>a.id===id);
  return {observedUtc:new Date().toISOString(),foreignCarrier,anonymousStatus:anonymous.status,anonymousCode,
    retainedDraft:{id:draft.id,candidateCount:draft.candidates?.length,sourceHashSha256:draft.sourceHashSha256},
    current:{connectionCurrent:read.connectionTestIsCurrent,state:read.state,environment:read.environment,enabled:read.integrationReadiness?.enabled,canExecute:read.integrationReadiness?.canExecute,blockers:read.integrationReadiness?.blockers},
    liveShipmentEffects:0,geographicPublications:0};
})

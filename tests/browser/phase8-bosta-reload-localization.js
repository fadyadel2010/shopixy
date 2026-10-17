async page => {
  const id='14030fd4-b1b5-4fed-bea5-80fb735c5f30';
  const language=await page.evaluate(async()=>(await import('/src/i18n/config.js')).default.language);
  const mutations=[]; let providerReads=0;
  const onRequest=r=>{const path=r.url().replace(/^https?:\/\/[^/]+/,'').split('?')[0];if(path.includes('/bosta/')&&r.method()==='GET')providerReads++;if(!['GET','HEAD','OPTIONS'].includes(r.method())&&/\/api\/admin\/(shipping|shipments|orders|carriers)/.test(path))mutations.push({method:r.method(),path});};
  page.on('request',onRequest);
  try {
    const before=await page.evaluate(async id=>{const r=await fetch('/api/admin/shipping/carrier-accounts',{cache:'no-store',credentials:'include'});const a=(await r.json()).find(a=>a.id===id);return {state:a.state,environment:a.environment,credentialReference:a.activeCredentialReferenceId,fields:a.savedCredentialFields,enabled:a.integrationReadiness.enabled,connectionCurrent:a.connectionTestIsCurrent,testId:a.lastConnectionTest?.id};},id);
    await page.reload();
    await page.evaluate(async()=>(await import('/src/i18n/config.js')).default.changeLanguage('en'));
    await page.getByRole('tab',{name:'Carrier Accounts',exact:true}).click();
    const card=page.locator('article').filter({has:page.getByRole('heading',{name:'Bosta',exact:true})});
    await card.getByText('Connection successful; booking setup incomplete',{exact:true}).waitFor();
    const en={connectedIncomplete:await card.getByText('Connection successful; booking setup incomplete',{exact:true}).isVisible(),nativeReady:await card.getByRole('region',{name:'Bosta destination references'}).isVisible(),savedKeyMasked:await card.locator('input[name=ApiKey]').inputValue()==='',activationIsExplicit:await card.getByRole('button',{name:'Activate',exact:true}).isVisible()};
    await page.evaluate(async()=>(await import('/src/i18n/config.js')).default.changeLanguage('ar'));
    await card.getByText('الاتصال ناجح؛ إعداد الحجز غير مكتمل',{exact:true}).waitFor();
    const ar={connectedIncomplete:await card.getByText('الاتصال ناجح؛ إعداد الحجز غير مكتمل',{exact:true}).isVisible(),nativeReady:await card.getByRole('region',{name:'مراجع وجهات بوسطة'}).isVisible(),direction:await page.locator('html').getAttribute('dir')};
    const after=await page.evaluate(async id=>{const r=await fetch('/api/admin/shipping/carrier-accounts',{cache:'no-store',credentials:'include'});const a=(await r.json()).find(a=>a.id===id);return {state:a.state,environment:a.environment,credentialReference:a.activeCredentialReferenceId,fields:a.savedCredentialFields,enabled:a.integrationReadiness.enabled,connectionCurrent:a.connectionTestIsCurrent,testId:a.lastConnectionTest?.id};},id);
    if(JSON.stringify(before)!==JSON.stringify(after)||after.enabled||providerReads||mutations.length)throw new Error('Reload changed saved configuration or caused provider/mutation calls');
    return {observedUtc:new Date().toISOString(),en,ar,configurationPreserved:true,connectionPreserved:true,activationUnchanged:true,providerReads,mutations,liveShipmentEffects:0};
  } finally {page.removeListener('request',onRequest);await page.evaluate(async lang=>(await import('/src/i18n/config.js')).default.changeLanguage(lang),language);}
}

async page => {
  const checks=[],failures=[],errors=[],requests=[];
  const marker='P7QA20261018-'+Date.now();
  const check=(name,passed,extra={})=>{checks.push({name,passed:!!passed,...extra});if(!passed)throw new Error(name);};
  const onError=e=>errors.push(e.message.split('\n')[0]); page.on('pageerror',onError);
  let original,candidate,providerCandidate,originalGoogle,storefront,diagnostic;
  const api=async(method,path,data,revision)=>await page.evaluate(async({method,path,data,revision})=>{
    const {default:client}=await import('/src/services/api.js');
    const r=await client.request({method,url:path,data,headers:revision?{'If-Match':'"'+revision+'"'}:{},validateStatus:()=>true});
    return {status:r.status,data:r.data};
  },{method,path,data,revision});
  const getConfig=async()=>{const r=await api('get','/admin/store-config');if(r.status!==200)throw new Error('Configuration read unavailable');return r.data;};
  const getGoogle=async()=>{const r=await api('get','/admin/storefront/auth-providers/Google');if(r.status!==200)throw new Error('Provider read unavailable');return r.data;};
  const publicGet=async path=>{const r=await page.context().request.get('http://localhost:5010'+path,{headers:{Host:'ahramstore.localhost:5010'}});return {status:r.status(),data:await r.json()};};
  const comparison=x=>JSON.stringify(Object.fromEntries(Object.entries(x).filter(([k])=>!['commerceAi','rowVersion','updatedAtUtc'].includes(k))));
  try {
    await page.goto('http://localhost:5173/ecommerce/commerce-ai'); await page.getByRole('switch',{name:'Enable Shop Assistant'}).waitFor();
    original=await getConfig(); originalGoogle=await getGoogle();
    check('Read current AI configuration without credentials',original.commerceAi&&typeof original.commerceAi.enabled==='boolean');
    if(original.commerceAi.enabled)throw new Error('This harness requires the recorded disabled AI baseline; refusing to overwrite concurrent state');
    await page.getByRole('switch',{name:'Enable Shop Assistant'}).click();
    await page.getByRole('button',{name:/Compact Icon/}).click();
    await page.getByRole('button',{name:'Bottom Left',exact:true}).click();
    await page.getByRole('radio',{name:/Professional/}).check();
    if(!(await page.getByRole('switch',{name:'Automatic Greeting'}).getAttribute('aria-checked')==='true'))await page.getByRole('switch',{name:'Automatic Greeting'}).click();
    await page.getByRole('textbox',{name:'Message',exact:true}).fill(marker+' مرحباً');
    const savedResponse=page.waitForResponse(r=>r.url().endsWith('/api/admin/store-config/commerce-ai')&&r.request().method()==='PUT');
    await page.getByRole('button',{name:'Save Changes',exact:true}).click(); const saved=await savedResponse;
    check('Actual UI narrow AI save accepted',saved.status()===200,{status:saved.status()});
    await page.getByRole('button',{name:'Save Changes',exact:true}).waitFor();
    candidate=(await getConfig()).commerceAi; const accepted=await getConfig();
    check('Persisted AI fields and new revision match UI',candidate.enabled&&candidate.widgetStyle==='compact'&&candidate.widgetPosition==='bottom-left'&&candidate.personality==='professional'&&candidate.autoGreetingMessage===marker+' مرحباً'&&accepted.rowVersion!==original.rowVersion,{beforeRevision:original.rowVersion,acceptedRevision:accepted.rowVersion});
    check('AI-only save preserves every unrelated returned StoreConfig field',comparison(original)===comparison(accepted));
    const pub=await publicGet('/api/storefront/store-config');
    check('Actual public SQL config consumes accepted AI settings',pub.status===200&&pub.data.commerceAiEnabled&&pub.data.commerceAiWidgetStyle==='compact'&&pub.data.commerceAiAutoGreetingMessage===marker+' مرحباً');
    const stale=await api('put','/admin/store-config/commerce-ai',original.commerceAi,original.rowVersion);
    check('Stale reviewed AI revision rejected',stale.status===409,{status:stale.status});
    const invalid=await api('put','/admin/store-config/commerce-ai',{...candidate,widgetStyle:'unknown'},accepted.rowVersion);
    check('Unsupported AI configuration rejected server-side',invalid.status===400,{status:invalid.status});
    const unsupported=await api('put','/admin/store-config/commerce-ai',{...candidate,provider:'invented-provider'},accepted.rowVersion);
    check('Unsupported provider field cannot be silently accepted by experience API',unsupported.status===400,{status:unsupported.status});
    storefront=await page.context().newPage();
    // Every inference attempt from this test tab is intercepted. The configured
    // remote provider is never invoked to turn this UI check green.
    await storefront.route('**/api/storefront/chat**',async route=>{requests.push({path:'/api/storefront/chat',classification:'controlled HTTP503; no model execution'});await route.fulfill({status:503,contentType:'application/json',body:JSON.stringify({type:'message',message:'CONTROLLED_PROVIDER_UNAVAILABLE',isFallback:true})});});
    await storefront.goto('http://ahramstore.localhost:5174/');
    await storefront.getByRole('button',{name:'Open chat',exact:true}).waitFor({timeout:30000});
    check('Actual Storefront consumes configured widget style and position',await storefront.locator('.chat-widget--pos-bottom-left .chat-widget__trigger--compact').count()===1);
    await storefront.getByRole('button',{name:'Open chat',exact:true}).click();
    await storefront.getByText(marker+' مرحباً',{exact:true}).waitFor();
    check('Configured Unicode greeting reaches actual Storefront widget',true);
    await storefront.locator('.chat-input__field').fill('P7QA controlled failure; do not call a model');
    await storefront.getByRole('button',{name:'Send',exact:true}).click();
    await storefront.locator('.chat-widget__error').waitFor();
    check('Controlled inference failure is not presented as successful inference',requests.length===1&&await storefront.locator('.chat-message--fallback').count()===1);
    await storefront.getByRole('button',{name:'Close',exact:true}).click();
    await page.goto('http://localhost:5173/ecommerce/auth-providers'); await page.getByRole('heading',{name:'Google',exact:true}).waitFor();
    const google=page.locator('.provider-card').filter({has:page.getByRole('heading',{name:'Google',exact:true})});
    check('Provider secret readback is write-only',!('clientSecret' in originalGoogle)&&!('clientSecretEncrypted' in originalGoogle)&&originalGoogle.hasClientSecret===false);
    if(originalGoogle.isEnabled||originalGoogle.clientId||originalGoogle.hasClientSecret)throw new Error('Provider baseline changed; refusing to overwrite');
    await google.getByRole('button',{name:'Edit',exact:true}).click();
    check('Secret editor is blank, not a submitted mask',await google.getByLabel('Client Secret',{exact:true}).inputValue()==='');
    await google.getByLabel('Client ID',{exact:true}).fill(marker);
    await google.getByLabel('Additional Scopes',{exact:true}).fill('email,profile');
    const providerSave=page.waitForResponse(r=>r.url().endsWith('/api/admin/storefront/auth-providers/Google')&&r.request().method()==='PUT');
    await google.getByRole('button',{name:'Save',exact:true}).click(); check('Actual provider UI configuration save accepted',(await providerSave).status()===200);
    providerCandidate=await getGoogle();
    check('Fresh provider readback persists synthetic identity, without enabling or adding a secret',providerCandidate.clientId===marker&&providerCandidate.additionalScopes==='email,profile'&&!providerCandidate.isEnabled&&!providerCandidate.hasClientSecret,{id:providerCandidate.id,revision:providerCandidate.revision});
    const noSecret=await api('post','/admin/storefront/auth-providers/Google/enable',{},providerCandidate.revision);
    check('Enable fails closed without credentials',noSecret.status===400,{status:noSecret.status});
    const publicProviders=await publicGet('/api/storefront/auth/providers');
    check('Actual public login provider listing matches disabled/unconfigured state',publicProviders.status===200&&!publicProviders.data.google&&!publicProviders.data.facebook&&!('clientSecret' in publicProviders.data));
    const unknown=await api('get','/admin/storefront/auth-providers/999');check('Unknown numeric provider fails closed after repair',unknown.status===400,{status:unknown.status});
    const staleProvider=await api('put','/admin/storefront/auth-providers/Google',{clientId:'must-not-save'},'0');
    check('Stale provider revision rejected',staleProvider.status===409,{status:staleProvider.status});
    const mask=await api('post','/admin/storefront/auth-providers/Google/rotate-secret',{clientSecret:'********'},providerCandidate.revision);
    check('Mask cannot become a stored credential',mask.status===400,{status:mask.status});
    const badScope=await api('put','/admin/storefront/auth-providers/Google',{additionalScopes:'arbitrary-admin-permission'},providerCandidate.revision);
    check('Unsupported OAuth scopes rejected server-side',badScope.status===400,{status:badScope.status});
  } catch(e){failures.push(e.message.split('\n')[0]);if(storefront)diagnostic=await storefront.locator('.chat-widget').innerText().catch(()=> 'Widget unavailable');}
  finally {
    if(storefront)await storefront.close();
    try {
      if(candidate&&original){const fresh=await getConfig();if(JSON.stringify(fresh.commerceAi)!==JSON.stringify(candidate))throw new Error('Concurrent AI change; restoration refused');const restored=await api('put','/admin/store-config/commerce-ai',original.commerceAi,fresh.rowVersion);check('Fresh-version AI restoration accepted',restored.status===200,{restoredRevision:restored.data.rowVersion});check('Independent fresh AI readback equals original business configuration',JSON.stringify((await getConfig()).commerceAi)===JSON.stringify(original.commerceAi));}
      if(providerCandidate&&originalGoogle){const fresh=await getGoogle();if(fresh.clientId!==marker||fresh.isEnabled||fresh.hasClientSecret)throw new Error('Concurrent provider change; restoration refused');const restored=await api('put','/admin/storefront/auth-providers/Google',{clientId:originalGoogle.clientId??'',additionalScopes:originalGoogle.additionalScopes??''},fresh.revision);check('Fresh-version provider restoration accepted',restored.status===200,{retainedSyntheticRowId:fresh.id,restoredRevision:restored.data.settings?.revision});const final=await getGoogle();check('Fresh provider readback is disabled and unconfigured; retained row accurately recorded',!final.isEnabled&&!final.clientId&&!final.hasClientSecret&&!final.additionalScopes);}
    }catch(e){failures.push('Restoration: '+e.message.split('\n')[0]);}
    page.off('pageerror',onError);
  }
  return {observedUtc:new Date().toISOString(),marker,checks,failures,errors,requests,diagnostic,originalAi:original?.commerceAi,originalRevision:original?.rowVersion,providerSyntheticId:providerCandidate?.id,originalProvider:{isEnabled:originalGoogle?.isEnabled,hasClientSecret:originalGoogle?.hasClientSecret,hasClientId:!!originalGoogle?.clientId},noExternalInference:true};
}

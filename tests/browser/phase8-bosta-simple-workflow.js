async page => {
  const id = '14030fd4-b1b5-4fed-bea5-80fb735c5f30';
  const originalLanguage = await page.evaluate(async () => (await import('/src/i18n/config.js')).default.language);
  const reads = [], writes = [], blocked = [];
  const guard = async route => {
    const r = route.request(), path = r.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0];
    if (!['GET','HEAD','OPTIONS'].includes(r.method()) && /^\/api\/(admin\/(shipping|shipments|orders|carriers)|shippingmethods)/.test(path)) {
      const allowed = path === `/api/admin/shipping/carrier-accounts/${id}/connection-tests` || path === `/api/admin/shipping/carrier-accounts/${id}/bosta/destinations/validate`;
      if (!allowed) { blocked.push({ method: r.method(), path }); return route.abort(); }
      writes.push({ method: r.method(), path });
    }
    if (r.method() === 'GET' && path.includes('/bosta/')) reads.push(path);
    return route.continue();
  };
  await page.route('**/api/**', guard);
  const read = () => page.evaluate(async id => {
    const get = async path => { const r = await fetch(path, { credentials: 'include', cache: 'no-store' }); if (!r.ok) throw new Error('Read failed: '+r.status); return r.json(); };
    const a = (await get('/api/admin/shipping/carrier-accounts')).find(a => a.id === id);
    const c = await get('/api/admin/carriers/bosta');
    const o = await get('/api/admin/orders/ac89be7f-5e8c-4834-bcec-a1d14d5af2e6');
    // Private address/contact projections stay in browser memory and are never returned.
    const config = { ...c }; for (const key of ['lastValidatedAtUtc','lastValidationSucceeded','lastValidationError','validationMatchesCurrentConfiguration','readiness']) delete config[key];
    return { config, account: { id:a.id, environment:a.environment, state:a.state, activeCredentialReferenceId:a.activeCredentialReferenceId,
      warehouseAssignments:a.warehouseAssignments, policies:a.policies, serviceLevels:a.serviceLevels, savedCredentialFields:a.savedCredentialFields },
      connectionTestIsCurrent:a.connectionTestIsCurrent, lastConnectionTest:a.lastConnectionTest, readiness:a.integrationReadiness,
      protectedOrder:{ id:o.orderId,status:o.status,paymentStatus:o.paymentStatus,total:o.totals?.total,shippingMethodId:o.shippingMethodId,warehouseId:o.warehouseId,shipmentId:o.shipmentId } };
  }, id);
  try {
    await page.evaluate(async () => (await import('/src/i18n/config.js')).default.changeLanguage('en'));
    await page.getByRole('tab', { name:'Carrier Accounts',exact:true }).click();
    const card = page.locator('article').filter({has:page.getByRole('heading',{name:'Bosta',exact:true})});
    await card.getByRole('button',{name:'Test Connection',exact:true}).waitFor();
    const before = await read();
    const renderingProviderCalls = reads.length;
    const [saveResponse] = await Promise.all([page.waitForResponse(r=>r.url().endsWith(`/${id}/connection-tests`) && r.request().method()==='POST'),card.getByRole('button',{name:'Save',exact:true}).click()]);
    await card.getByText('Credentials saved. Connection not tested.',{exact:true}).waitFor();
    const afterSave = await read();
    const [testResponse] = await Promise.all([page.waitForResponse(r=>r.url().endsWith(`/${id}/connection-tests`) && r.request().method()==='POST'),card.getByRole('button',{name:'Test Connection',exact:true}).click()]);
    await card.getByText('Connection successful; booking setup incomplete',{exact:true}).waitFor();
    const [pickupResponse] = await Promise.all([page.waitForResponse(r=>r.url().endsWith('/bosta/pickup-locations')),card.getByRole('button',{name:'Load saved-account pickup locations',exact:true}).click()]);
    const pickup = await pickupResponse.json();
    await card.getByText(/^Provider default verified:/).waitFor();
    const automaticPickupSelection = await card.getByRole('combobox',{name:'Choose verified pickup location',exact:true}).inputValue();
    const native = card.getByRole('region',{name:'Bosta destination references',exact:true});
    const [cityResponse] = await Promise.all([page.waitForResponse(r=>r.url().endsWith('/bosta/native-geography')),native.getByRole('button',{name:'Load Bosta cities',exact:true}).click()]);
    const cities = await cityResponse.json();
    await native.getByRole('combobox',{name:'Bosta city',exact:true}).selectOption('FceDyHXwpSYYF9zGW');
    const [districtResponse] = await Promise.all([page.waitForResponse(r=>r.url().includes('/bosta/native-geography?cityId=')),native.getByRole('button',{name:'Load Bosta districts',exact:true}).click()]);
    const districts = await districtResponse.json();
    await native.getByRole('combobox',{name:'Bosta district',exact:true}).selectOption('VPRW3NEl5WQ');
    const [validationResponse] = await Promise.all([page.waitForResponse(r=>r.url().endsWith('/bosta/destinations/validate')),native.getByRole('button',{name:'Validate Bosta destination',exact:true}).click()]);
    const validation = await validationResponse.json();
    await native.getByText('Bosta destination verified and serviceable; shipment and account checks are still required.',{exact:true}).waitFor();
    const after = await read();
    const unavailable = districts.places.find(p=>p.type==='district'&&!p.dropOffAvailable);
    const negative = unavailable ? await page.evaluate(async ({id,city,district}) => {
      const {validateBostaNativeDestination} = await import('/src/modules/shipping-administration/bostaNativeApi.js');
      try { await validateBostaNativeDestination(id,city,district); return {rejected:false}; }
      catch(e) { return {rejected:true,status:e.response?.status,code:e.response?.data?.code}; }
    }, {id,city:'FceDyHXwpSYYF9zGW',district:unavailable.id}) : null;
    const enabledUnavailableOptions = await native.getByRole('combobox',{name:'Bosta district',exact:true}).locator('option').evaluateAll(options=>options.filter(o=>o.textContent.includes('Destination is not serviceable')&&!o.disabled).length);
    const comparisons = { configurationUnchanged:JSON.stringify(before.config)===JSON.stringify(after.config),
      accountConfigurationUnchanged:JSON.stringify(before.account)===JSON.stringify(after.account),
      noOpSavePreservesConnection:afterSave.connectionTestIsCurrent && before.lastConnectionTest?.id===afterSave.lastConnectionTest?.id,
      protectedOrderUnchanged:JSON.stringify(before.protectedOrder)===JSON.stringify(after.protectedOrder) };
    if (Object.values(comparisons).some(v=>!v)) throw new Error('Preservation/readback mismatch');
    if (after.readiness.enabled || after.readiness.canExecute || after.readiness.blockers.includes('CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED')) throw new Error('Activation/readiness separation failed');
    return {observedUtc:new Date().toISOString(),loadedApi:true,statuses:{save:saveResponse.status(),test:testResponse.status(),pickup:pickupResponse.status(),cities:cityResponse.status(),districts:districtResponse.status(),validation:validationResponse.status()},
      comparisons,renderingProviderCalls,automaticPickupSelection,pickupSelection:pickup.pickupSelection,
      providerCityCount:cities.places.filter(p=>p.type==='city').length,providerDistrictCount:districts.places.filter(p=>p.type==='district').length,
      acceptedDestination:{cityId:validation.city?.id,districtId:validation.district?.id,validated:validation.validated,serviceable:validation.serviceable,bookingReady:validation.bookingReady},
      unavailableDestinationRejection:negative,enabledUnavailableOptions,connectionCurrent:after.connectionTestIsCurrent,lastConnectionTest:after.lastConnectionTest,
      currentAccount:after.account,currentReadiness:after.readiness,protectedOrder:after.protectedOrder,reads,writes,blocked,
      liveShipmentEffects:0,canonicalPublications:0,activation:false};
  } finally {
    await page.unroute('**/api/**',guard);
    await page.evaluate(async lang=>(await import('/src/i18n/config.js')).default.changeLanguage(lang),originalLanguage);
  }
}

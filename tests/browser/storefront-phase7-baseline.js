async page => {
  await page.goto('http://localhost:5173/ecommerce/auth-providers');
  await page.locator('main h1').waitFor();
  return await page.evaluate(async () => {
    const {default:api} = await import('/src/services/api.js');
    const c = (await api.get('/admin/store-config')).data;
    const p = (await api.get('/admin/storefront/auth-providers')).data;
    const info = (await api.get('/admin/commerce-ai')).data;
    const unknown = await api.get('/admin/storefront/auth-providers/999', {validateStatus:()=>true});
    return {observedUtc:new Date().toISOString(),rowVersion:c.rowVersion,ai:c.commerceAi,
      providers:p.providers.map(x=>({provider:x.provider,id:x.id,isEnabled:x.isEnabled,hasClientId:!!x.clientId,hasClientSecret:x.hasClientSecret,additionalScopes:x.additionalScopes,revision:x.revision})),
      modelInfo:info.models, unknownProviderStatus:unknown.status, unknownProvider:unknown.data.provider};
  });
}

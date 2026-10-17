async page => {
  const checks = [];
  const { default: api } = await page.evaluate(async () => ({default: null}));
  const admin = await page.evaluate(async () => {
    const { default: client } = await import('/src/services/api.js');
    const read = async url => (await client.get(url)).data;
    return { settings: await read('/admin/analytics/settings'), pixel: await read('/admin/pixel-tracking/config'), costs: await read('/admin/analytics/campaign-costs?period=last_30_days'), roi: await read('/admin/analytics/roi/campaigns?period=last_30_days') };
  });
  if (['google','meta','tikTok'].some(p => admin.settings[p]?.enabled)) throw new Error('External provider enabled: establish controlled boundary before traffic');
  for (const route of ['/ecommerce/pixel-tracking','/analytics/funnels','/analytics/attribution','/analytics/attribution/costs','/analytics/attribution/roi']) {
    await page.goto('http://localhost:5173' + route);
    await page.locator('main h1').waitFor();
    checks.push({route, heading: await page.locator('main h1').innerText()});
  }
  const storefront = await page.context().newPage();
  const events = [];
  storefront.on('request', req => {
    if (req.url().includes('/api/storefront/analytics/event') && req.method() === 'POST') events.push(req.postDataJSON());
  });
  try {
    await storefront.goto('http://ahramstore.localhost:5174/?utm_source=p6qa&utm_medium=cpc&utm_campaign=p6-baseline');
    await storefront.waitForTimeout(12000);
    checks.push({homeEvents: events.length});
    await storefront.goto('http://ahramstore.localhost:5174/products/cert-media-variable-20261007?utm_source=p6qa&utm_medium=cpc&utm_campaign=p6-baseline');
    await storefront.waitForTimeout(12000);
    checks.push({productEvents: events.length});
    return {utc: new Date().toISOString(), admin, checks, events};
  } finally { await storefront.close(); }
}

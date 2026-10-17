async page => {
  const prefix = 'p6qa20261018-tracking-' + Date.now();
  const checks = [], failures = [], errors = [], consoleErrors = [], events = [], responses = [], external = [], oauthSdkRequests = [];
  const check = (name, passed, extra = {}) => { checks.push({ name, passed: !!passed, ...extra }); if (!passed) throw new Error(name); };
  const context = await page.context().browser().newContext({ viewport: { width: 1440, height: 900 } });
  const storefront = await context.newPage();
  storefront.on('pageerror', e => errors.push(e.message.split('\n')[0]));
  storefront.on('console', msg => { if (msg.type() === 'error') consoleErrors.push(msg.text().split('\n')[0]); });
  storefront.on('request', req => {
    if (req.url().includes('/api/storefront/analytics/event') && req.method() === 'POST') {
      const data = req.postDataJSON(); events.push(...(data.events || [data]));
    }
    if (/connect\.facebook\.net\/.*\/sdk\.js/.test(req.url())) oauthSdkRequests.push(req.url().split('?')[0]);
    if (/google-analytics\.com\/(?:g\/collect|mp\/)|facebook\.com\/tr|connect\.facebook\.net\/.*fbevents|analytics\.tiktok/.test(req.url())) external.push(req.url().split('?')[0]);
  });
  storefront.on('response', r => { if (r.url().includes('/api/storefront/analytics/event')) responses.push({ status: r.status(), path: r.url().split('?')[0] }); });
  const moduleCall = (method) => storefront.evaluate(async method => {
    const loaded = performance.getEntriesByType('resource').map(e => e.name).find(n => /\/src\/services\/api\/storefrontApi\.ts(?:\?|$)/.test(n));
    const api = await import(loaded || '/src/services/api/storefrontApi.ts');
    return api[method]();
  }, method);
  const flush = async () => {
    await storefront.evaluate(async () => {
      const loaded = performance.getEntriesByType('resource').map(e => e.name).find(n => /\/src\/analytics\/AnalyticsQueue\.ts(?:\?|$)/.test(n));
      await (await import(loaded || '/src/analytics/AnalyticsQueue.ts')).flushAnalyticsQueue();
    });
    await storefront.waitForTimeout(400);
  };
  let cartStartedEmpty = false;
  try {
    await storefront.goto('http://ahramstore.localhost:5174/?utm_source=P6QA&utm_medium=CPC&utm_campaign=' + prefix + '&utm_term=qa-term&utm_content=qa-creative&token=synthetic-do-not-forward#synthetic-do-not-forward');
    await storefront.getByRole('link', { name: 'AhramStore', exact: true }).waitFor();
    cartStartedEmpty = (await moduleCall('fetchCart')).items.length === 0;
    check('New anonymous Storefront session starts with empty persisted cart', cartStartedEmpty);
    await flush();
    const homeViews = events.filter(e => e.type === 'PageView');
    check('One actual initial home PageView, with only allowlisted campaign query', homeViews.length === 1 && homeViews[0].context.pageUrl.includes('utm_campaign=' + prefix) && !homeViews[0].context.pageUrl.includes('token=') && !homeViews[0].context.pageUrl.includes('#'), { homeViews });
    await storefront.getByRole('link', { name: 'Shop', exact: true }).click();
    await storefront.getByRole('heading', { name: 'All Products', exact: true }).waitFor();
    await flush();
    check('One actual catalog-route PageView', events.filter(e => e.type === 'PageView' && /\/products(?:\?|$)/.test(e.context.pageUrl)).length === 1);
    const card = storefront.locator('article').filter({ has: storefront.getByRole('heading', { name: 'CERT Media Simple 20261007', exact: true }) }).locator('..');
    await card.getByRole('button', { name: 'Add to Cart', exact: true }).click();
    await storefront.waitForTimeout(1500);
    const added = await moduleCall('fetchCart');
    check('Actual catalog quick-add persists exactly one item', added.items.length === 1 && added.itemCount === 1, { cartId: added.cartId, ownerType: added.ownerType, itemCount: added.itemCount });
    await flush();
    check('Catalog quick-add emits exactly one AddToCart', events.filter(e => e.type === 'AddToCart').length === 1);
    await storefront.getByRole('link', { name: /CERT Media Simple 20261007/ }).first().click();
    await storefront.getByRole('heading', { name: 'CERT Media Simple 20261007', exact: true }).waitFor();
    await flush();
    check('Product detail emits one PageView and one ProductView despite multiple runtime consumers', events.filter(e => e.type === 'PageView' && e.context.pageUrl.includes('/products/cert-media-simple-20261007')).length === 1 && events.filter(e => e.type === 'ProductView').length === 1);
    await storefront.getByRole('link', { name: /Shopping cart/ }).first().click();
    await storefront.locator('main').getByRole('heading').first().waitFor();
    await flush();
    check('Cart route has one PageView', events.filter(e => e.type === 'PageView' && /\/cart(?:\?|$)/.test(e.context.pageUrl)).length === 1);
    // Navigate through the actual router without submitting an order.
    await storefront.locator('main').getByRole('button', { name: /checkout/i }).first().click();
    await storefront.waitForURL('**/checkout');
    await storefront.waitForTimeout(2000); await flush();
    check('Checkout entry has one PageView and one BeginCheckout; no purchase from browser', events.filter(e => e.type === 'PageView' && /\/checkout(?:\?|$)/.test(e.context.pageUrl)).length === 1 && events.filter(e => e.type === 'BeginCheckout').length === 1 && !events.some(e => e.type === 'PurchaseCompleted'));
    check('All event IDs distinct and all accepted by actual ingestion', new Set(events.map(e => e.eventId)).size === events.length && responses.length > 0 && responses.every(r => r.status === 202), { responses });
    const replay = events[0];
    const replayStatus = await storefront.evaluate(async event => {
      const loaded = performance.getEntriesByType('resource').map(e => e.name).find(n => /\/src\/analytics\/AnalyticsClient\.ts(?:\?|$)/.test(n));
      return (await (await import(loaded || '/src/analytics/AnalyticsClient.ts')).sendAnalyticsEvent(event)).accepted;
    }, replay);
    check('Same event-ID replay is accepted at ingress for durable consumer deduplication', replayStatus);
    check('No browser advertising SDK/transport initialized', external.length === 0, { external });
    await storefront.screenshot({ path: '.ai/evidence/storefront-phase6-20261018/storefront-checkout-tracking.png' });
  } catch (e) { failures.push(e.message.split('\n')[0]); }
  finally {
    if (cartStartedEmpty) {
      try { await moduleCall('clearCart'); check('Fresh cart readback after exact session cleanup is empty', (await moduleCall('fetchCart')).items.length === 0); }
      catch (e) { failures.push('Cart cleanup: ' + e.message.split('\n')[0]); }
    }
    await context.close();
  }
  return { prefix, checks, failures, errors, consoleErrors, events, responses, external, oauthSdkRequests, submittedOrders: 0 };
}

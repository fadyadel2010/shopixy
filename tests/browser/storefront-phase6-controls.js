async page => {
  const checks = [], failures = [], writes = [], errors = [];
  const check = (name, passed, extra = {}) => { checks.push({ name, passed: !!passed, ...extra }); if (!passed) throw new Error(name); };
  const onError = e => errors.push(e.message.split('\n')[0]);
  page.on('pageerror', onError);
  const guard = route => {
    const req = route.request();
    if (/\/api\/admin\/(analytics|pixel-tracking)/.test(req.url()) && !['GET','HEAD','OPTIONS'].includes(req.method())) { writes.push(req.url().split('?')[0]); return route.abort(); }
    return route.continue();
  };
  await page.route('**/api/**', guard);
  const pattern = '**/api/admin/**';
  let failedPath = '';
  const failRead = route => {
    const req = route.request();
    if (req.method() === 'GET' && req.url().includes(failedPath)) return route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'P6 QA controlled unavailable' }) });
    return route.fallback();
  };
  const readResponse = fragment => page.waitForResponse(r => r.url().includes(fragment) && r.status() === 200);
  try {
    await page.goto('http://localhost:5173/analytics/funnels/purchase_funnel');
    await page.getByRole('button', { name: 'Last hour', exact: true }).waitFor();
    await page.getByRole('button', { name: 'Last hour', exact: true }).click();
    const funnelResult = readResponse('/purchase_funnel/realtime?windowMinutes=15');
    await page.getByRole('button', { name: 'Last 15 minutes', exact: true }).click();
    check('Actual funnel time selector sends 15-minute request', (await (await funnelResult).json()).windowMinutes === 15);

    await page.goto('http://localhost:5173/analytics/attribution');
    await page.getByRole('button', { name: 'Last Touch', exact: true }).click();
    const [firstResult] = await Promise.all([
      page.waitForResponse(r => r.url().includes('/attribution/summary') && /[?&]model=FirstTouch(?:&|$)/.test(r.url()) && r.status() === 200),
      page.getByRole('button', { name: /^First Touch / }).click(),
    ]);
    check('Attribution model control fetches FirstTouch', (await firstResult.json()).model === 'FirstTouch');
    await page.getByRole('button', { name: /^Last 7 days$/i }).click();
    const [monthResult] = await Promise.all([
      page.waitForResponse(r => r.url().includes('/attribution/summary') && /[?&]period=last_month(?:&|$)/.test(r.url()) && r.status() === 200),
      page.getByRole('button', { name: /^Last month$/i }).click(),
    ]);
    const month = await monthResult.json();
    check('Attribution date control uses actual UTC last-month range', month.period === 'last_month' && month.startDate.endsWith('T00:00:00Z') && month.endDate.endsWith('T00:00:00Z'), { start: month.startDate, end: month.endDate });

    await page.goto('http://localhost:5173/analytics/attribution/roi');
    await page.getByRole('combobox').nth(0).selectOption('last_month');
    const roiResult = page.waitForResponse(r => r.url().includes('/roi/campaigns') && /[?&]model=FirstTouch(?:&|$)/.test(r.url()) && /[?&]period=last_month(?:&|$)/.test(r.url()) && r.status() === 200);
    await page.getByRole('combobox').nth(1).selectOption('FirstTouch');
    const roi = await (await roiResult).json();
    check('ROI model and date selectors fetch the combined selected scope', roi.model === 'FirstTouch' && roi.period === 'last_month');

    for (const [path, api, label] of [
      ['/ecommerce/pixel-tracking','/pixel-tracking/config','pixel'],
      ['/analytics/funnels/purchase_funnel','/analytics/funnels/purchase_funnel/realtime','funnel'],
      ['/analytics/attribution','/analytics/attribution/','attribution'],
      ['/analytics/attribution/costs','/analytics/campaign-costs','costs'],
      ['/analytics/attribution/roi','/analytics/roi/campaigns','roi'],
    ]) {
      failedPath = api;
      await page.route(pattern, failRead);
      await page.goto('http://localhost:5173' + path);
      const message = label === 'pixel' ? page.getByText('Configuration unavailable') : page.locator('main').getByText('P6 QA controlled unavailable', { exact: true }).first();
      await message.waitFor();
      check(label + ' actual screen shows controlled initial-read failure', true);
      if (label === 'pixel') check('Pixel load failure cannot save fabricated defaults', await page.getByRole('button', { name: 'Save Configuration', exact: true }).isDisabled());
      await page.unroute(pattern, failRead);
      const recovered = readResponse(api);
      await page.getByRole('button', { name: label === 'pixel' ? 'Retry' : 'Refresh', exact: true }).click();
      await recovered;
      await message.waitFor({ state: 'hidden' });
      check(label + ' retry recovers through the real configured API', true);
    }
    check('No marketing mutations in filter/failure-control pass', writes.length === 0);
    check('No uncaught page exception during controls/failure recovery', errors.length === 0);
  } catch (e) { failures.push(e.message.split('\n')[0]); }
  finally { await page.unroute(pattern, failRead); await page.unroute('**/api/**', guard); page.off('pageerror', onError); }
  return { checks, failures, errors, writes, unavailableResponsesWereControlledHarnessInjection: true, utc: new Date().toISOString() };
}

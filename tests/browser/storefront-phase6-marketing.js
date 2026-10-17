async page => {
  const prefix = 'p6qa20261018-' + Date.now();
  const checks = [], failures = [], errors = [], requests = [], manifest = [];
  const check = (name, passed, extra = {}) => { checks.push({ name, passed: !!passed, ...extra }); if (!passed) throw new Error(name); };
  const onError = e => errors.push(e.message.split('\n')[0]);
  const onResponse = r => { if (r.url().includes('/api/')) requests.push({ path: r.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0], method: r.request().method(), status: r.status() }); };
  page.on('pageerror', onError); page.on('response', onResponse);
  const api = (method, url, data, headers = {}) => page.evaluate(async args => {
    const client = (await import('/src/services/api.js')).default;
    try { const r = await client.request(args); return { status: r.status, data: r.data }; }
    catch (e) { return { status: e.response?.status || 0, message: e.response?.data?.message || e.response?.data?.error }; }
  }, { method, url, data, headers });
  const read = async url => { const r = await api('get', url); if (r.status !== 200) throw new Error('Read failed ' + url + ' ' + r.status); return r.data; };
  const costUrl = '/admin/analytics/campaign-costs';
  const costs = async () => (await read(costUrl + '?period=last_30_days')).costs;
  const subset = c => ({ isEnabled: c.isEnabled, strategy: c.strategy, value: c.value, appliesTo: c.appliesTo });
  let original, candidate, absentAtStart = false;
  try {
    await page.goto('http://localhost:5173/ecommerce/pixel-tracking');
    await page.getByRole('switch').waitFor();
    original = subset(await read('/admin/pixel-tracking/config'));
    const settings = await read('/admin/analytics/settings');
    check('Actual AhramStore context, no external provider enabled', settings.tenantId === 'c9c5c1ae-949e-498e-bc63-07242daeb49d' && !settings.google?.enabled && !settings.meta?.enabled && !settings.tikTok?.enabled);
    candidate = { isEnabled: true, strategy: 'PERCENTAGE', value: 0, appliesTo: ['Google'] };
    if (!await page.getByRole('switch').getAttribute('aria-checked').then(x => x === 'true')) await page.getByRole('switch').click();
    await page.getByText('Percentage (%)', { exact: true }).click();
    await page.getByLabel('Override value').fill('0');
    for (const name of ['Meta (Facebook/Instagram)', 'Google Ads', 'TikTok Ads']) {
      const box = page.getByRole('checkbox', { name });
      if (await box.isChecked() !== (name === 'Google Ads')) await page.getByText(name, { exact: true }).click();
    }
    await page.getByRole('button', { name: 'Save Configuration', exact: true }).click();
    await page.getByRole('status').filter({ hasText: 'Pixel value configuration saved and verified' }).waitFor();
    check('Pixel zero override persisted with authoritative readback', JSON.stringify(subset(await read('/admin/pixel-tracking/config'))) === JSON.stringify(candidate));
    await page.reload(); await page.getByRole('switch').waitFor();
    check('Pixel enable/value survives actual reload', await page.getByRole('switch').getAttribute('aria-checked') === 'true' && await page.getByLabel('Override value').inputValue() === '0');
    await page.getByText('0.00', { exact: true }).waitFor();
    check('Actual zero preview is displayed', true);
    const invalidPixel = await api('put', '/admin/pixel-tracking/config', { ...candidate, strategy: '999' });
    check('Numeric undefined pixel enum is rejected', invalidPixel.status === 400, { status: invalidPixel.status });
    const invalidProvider = await api('put', '/admin/pixel-tracking/config', { ...candidate, appliesTo: ['999'] });
    check('Unknown pixel provider is rejected', invalidProvider.status === 400);
    await page.getByRole('switch').click();
    candidate = { ...candidate, isEnabled: false };
    await page.getByRole('button', { name: 'Save Configuration', exact: true }).click();
    await page.getByRole('status').filter({ hasText: 'Pixel value configuration saved and verified' }).waitFor();
    check('Disable means override off, not external event-delivery enablement', !(await read('/admin/pixel-tracking/config')).isEnabled);

    await page.goto('http://localhost:5173/analytics/attribution/costs');
    await page.getByRole('button', { name: 'Add Cost', exact: true }).waitFor();
    absentAtStart = !(await costs()).some(c => c.campaign.startsWith(prefix));
    check('Unique hosted cost scope starts absent', absentAtStart);
    const start = new Date().toISOString().slice(0, 10), end = new Date(Date.now() + 86400000).toISOString().slice(0, 10);
    for (const [suffix, amount] of [['main', '40'], ['cost-only', '15'], ['zero', '0']]) {
      await page.getByRole('button', { name: 'Add Cost', exact: true }).click();
      const modal = page.getByRole('dialog');
      await modal.getByLabel('Source', { exact: true }).fill('P6QA');
      await modal.getByLabel('Medium', { exact: true }).fill('CPC');
      await modal.getByLabel('Campaign', { exact: true }).fill(prefix + '-' + suffix);
      await modal.getByLabel('Period Start', { exact: true }).fill(start);
      await modal.getByLabel('Period End', { exact: true }).fill(end);
      await modal.getByLabel('Cost Amount', { exact: true }).fill(amount);
      await modal.getByLabel('Notes', { exact: true }).fill('SYNTHETIC QA / no ad spend incurred');
      await modal.getByRole('button', { name: 'Save', exact: true }).click();
      await modal.waitFor({ state: 'hidden' });
      await page.getByText(prefix + '-' + suffix, { exact: true }).waitFor();
      const row = (await costs()).find(c => c.campaign === prefix + '-' + suffix);
      manifest.push(row);
      check('UI create/readback ' + suffix, row && row.costAmount === Number(amount) && row.currency === 'EGP' && row.source === 'p6qa' && row.medium === 'cpc');
    }
    const main = manifest[0];
    await page.getByRole('row').filter({ hasText: main.campaign }).getByTitle('Edit', { exact: true }).click();
    let modal = page.getByRole('dialog');
    await modal.getByLabel('Campaign', { exact: true }).fill(prefix + '-edited');
    await modal.getByLabel('Cost Amount', { exact: true }).fill('25');
    await modal.getByRole('button', { name: 'Save', exact: true }).click();
    await modal.waitFor({ state: 'hidden' });
    await page.getByText(prefix + '-edited', { exact: true }).waitFor();
    const edited = (await costs()).find(c => c.campaign === prefix + '-edited'); manifest.push(edited);
    check('Actual edit replaces the intended row, preserves history and does not duplicate active spend', edited && edited.costAmount === 25 && edited.campaignCostId !== main.campaignCostId && !(await costs()).some(c => c.campaignCostId === main.campaignCostId));
    const invalid = await api('post', costUrl, { ...edited, campaign: prefix + '-invalid', costAmount: -1 });
    check('Negative cost denied', invalid.status === 400);
    const currency = await api('post', costUrl, { ...edited, campaign: prefix + '-currency', currency: 'USD' });
    check('Unsupported cross-currency cost denied', currency.status === 400);
    const stale = await api('put', costUrl + '/' + edited.campaignCostId, edited, { 'If-Match': '2000-01-01T00:00:00Z' });
    check('Reviewed stale edit denied without archiving current record', stale.status === 409 && (await costs()).some(c => c.campaignCostId === edited.campaignCostId), { status: stale.status });
    await page.reload(); await page.getByText(prefix + '-edited', { exact: true }).waitFor();
    check('Edited cost survives real reload', true);
    const roi = await read('/admin/analytics/roi/campaigns?period=last_30_days');
    const own = roi.campaigns.filter(c => c.campaign.startsWith(prefix));
    check('Independent hosted arithmetic: spend40/revenue0/profit-40/ROI-100 and zero-spend null', own.length === 3 && own.reduce((s,c) => s + c.cost, 0) === 40 && own.every(c => c.revenue === 0) && own.find(c => c.campaign.endsWith('-zero')).roiPercentage == null && own.filter(c => c.cost > 0).every(c => c.roiPercentage === -100), { own, totals: { spend: roi.totalCost, revenue: roi.totalRevenue, profit: roi.totalProfit, overallRoi: roi.overallRoi } });
    await page.goto('http://localhost:5173/analytics/attribution/roi');
    await page.getByText(prefix + '-edited', { exact: true }).waitFor();
    check('Actual ROI card shows server aggregate -100%', (await page.locator('main').innerText()).includes('-100.0%'));
    await page.screenshot({ path: '.ai/evidence/storefront-phase6-20261018/roi-hosted.png' });
    check('Unsupported attribution enum denied', (await api('get', '/admin/analytics/attribution/summary?model=999')).status === 400);
  } catch (e) { failures.push(e.message.split('\n')[0]); }
  finally {
    if (absentAtStart) {
      await page.goto('http://localhost:5173/analytics/attribution/costs');
      const owned = (await costs()).filter(c => c.campaign.startsWith(prefix));
      if (owned.length) {
        const target = owned[0];
        await page.getByText(target.campaign, { exact: true }).waitFor();
        await page.getByRole('row').filter({ hasText: target.campaign }).getByTitle('Delete', { exact: true }).click();
        await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
        check('UI delete cancellation preserves the selected cost', (await costs()).some(c => c.campaignCostId === target.campaignCostId));
        await page.getByRole('row').filter({ hasText: target.campaign }).getByTitle('Delete', { exact: true }).click();
        const archived = page.waitForResponse(r => r.url().endsWith(costUrl + '/' + target.campaignCostId) && r.request().method() === 'DELETE');
        await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click();
        check('Actual UI confirmation archives precisely the selected ID', (await archived).status() === 200);
        await page.getByText(target.campaign, { exact: true }).waitFor({ state: 'hidden' });
      }
      for (const cost of owned) {
        if (!(await costs()).some(c => c.campaignCostId === cost.campaignCostId)) continue;
        const result = await api('delete', costUrl + '/' + cost.campaignCostId);
        checks.push({ name: 'Supported exact-ID cost archive ' + cost.campaignCostId, passed: result.status === 200 });
      }
      check('Fresh API observer finds no active hosted fixture cost', !(await costs()).some(c => c.campaign.startsWith(prefix)));
      check('ROI refresh loses archived fixture spend', !(await read('/admin/analytics/roi/campaigns?period=last_30_days')).campaigns.some(c => c.campaign.startsWith(prefix)));
    }
    if (original && candidate) {
      const current = subset(await read('/admin/pixel-tracking/config'));
      if (JSON.stringify(current) === JSON.stringify(candidate)) {
        const result = await api('put', '/admin/pixel-tracking/config', original);
        check('Narrow original override restored only after current candidate ownership check', result.status === 204 && JSON.stringify(subset(await read('/admin/pixel-tracking/config'))) === JSON.stringify(original));
      } else if (JSON.stringify(current) !== JSON.stringify(original)) failures.push('Pixel restoration withheld: configuration changed outside the recorded candidate');
    }
    page.off('pageerror', onError); page.off('response', onResponse);
  }
  return { prefix, checks, failures, errors, manifest, originalPixel: original, finalPixel: await read('/admin/pixel-tracking/config'), requests };
}

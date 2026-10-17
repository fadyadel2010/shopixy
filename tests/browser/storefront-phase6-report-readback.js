async page => {
  const checks = [];
  const pending = page.waitForResponse(r => r.url().includes('/api/admin/analytics/funnels/purchase_funnel/realtime') && r.status() === 200);
  await page.goto('http://localhost:5173/analytics/funnels/purchase_funnel');
  const funnel = await (await pending).json();
  for (const step of funnel.steps) {
    const row = page.getByRole('row').filter({ has: page.getByText(step.eventType, { exact: true }) });
    await row.waitFor();
    const count = Number((await row.getByRole('cell').nth(3).innerText()).replace(/,/g, ''));
    if (count !== step.count) throw new Error('Funnel UI/API mismatch ' + step.eventType);
    checks.push({ eventType: step.eventType, displayed: count, api: step.count, passed: true });
  }
  const summaryResponse = page.waitForResponse(r => r.url().includes('/api/admin/analytics/attribution/summary') && r.status() === 200);
  await page.goto('http://localhost:5173/analytics/attribution');
  const summary = await (await summaryResponse).json();
  await page.getByText('Attributed Net Revenue', { exact: true }).waitFor();
  const revenue = Number((await page.getByText('Attributed Net Revenue', { exact: true }).locator('..').locator('p').nth(1).innerText()).replace(/[^\d.-]/g, ''));
  const orders = Number((await page.getByText('Attributed Purchases', { exact: true }).locator('..').locator('p').nth(1).innerText()).replace(/,/g, ''));
  if (revenue !== summary.totalRevenue || orders !== summary.totalOrders) throw new Error('Attribution UI/API totals mismatch');
  checks.push({ name: 'Attribution displayed revenue and purchases match API', revenue, orders, passed: true });
  if (summary.totalOrders === 0) {
    await page.getByText('No attributed orders yet', { exact: true }).waitFor();
    checks.push({ name: 'Actual attributed-empty UI agrees with API, not all-order revenue', passed: true });
  }
  return { checks, funnel, summary, utc: new Date().toISOString() };
}

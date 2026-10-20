// Actual recognized order -> immutable attribution -> manually declared QA cost
// -> ROI, then supported exact-ID archive/cart cleanup. No advertising provider.
async page => {
    const s = await page.evaluate(() => JSON.parse(sessionStorage.getItem('phase8-scenario')));
    const checks = [], failures = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const p = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const api = (method, path, data, version) => p.evaluate(async ({ method, path, data, version }) => {
        const a = (await import('/src/services/api.js')).default;
        try { const r = await a.request({ method, url: path, data, headers: version ? { 'If-Match': version } : {} }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, code: e.response?.data?.code }; }
    }, { method, path, data, version });
    let cost, roi;
    try {
        check('Exact completed order scope retained', s.orderId === '70461d1c-51a1-4161-987a-eae2d8cbd668' && !!s.settlementJournalId);
        const providers = await api('get', '/admin/analytics/settings');
        check('No advertising destination is enabled', providers.status === 200 && !providers.data.google?.enabled && !providers.data.meta?.enabled && !providers.data.tiktok?.enabled);
        let own;
        for (let i = 0; i < 30; i++) {
            const before = await api('get', '/admin/analytics/roi/campaigns?period=last_30_days&model=LastTouch');
            own = before.data.campaigns.find(x => x.campaign?.toLowerCase() === s.marker.toLowerCase());
            if (own?.revenue === 30) break;
            await page.waitForTimeout(1000);
        }
        check('Actual persisted purchase is attributed to entered campaign at SQL net revenue30', own?.revenue === 30 && own.source === 'p8qa', { beforeCost: own });
        const costs = await api('get', '/admin/analytics/campaign-costs?period=last_30_days');
        check('This exact campaign has no previous active QA cost', costs.status === 200 && !costs.data.costs.some(x => x.campaign.toLowerCase() === s.marker.toLowerCase()));
        const start = new Date().toISOString().slice(0, 10) + 'T00:00:00Z';
        const end = new Date(Date.now() + 86400000).toISOString().slice(0, 10) + 'T00:00:00Z';
        const created = await api('post', '/admin/analytics/campaign-costs', { source: own.source, medium: own.medium, campaign: own.campaign, periodStartUtc: start, periodEndUtc: end, costAmount: 12, currency: 'EGP', notes: 'P8QA synthetic internal cost declaration; no external advertising spend' });
        cost = created.data;
        check('Exact synthetic EGP12 declared cost persisted', created.status === 200 && !!cost.campaignCostId && cost.costAmount === 12, { costId: cost?.campaignCostId });
        s.costId = cost.campaignCostId;
        await page.evaluate(s => sessionStorage.setItem('phase8-scenario', JSON.stringify(s)), s);
        const report = await api('get', '/admin/analytics/roi/campaigns?period=last_30_days&model=LastTouch');
        roi = report.data.campaigns.find(x => x.campaign?.toLowerCase() === s.marker.toLowerCase());
        check('Hosted ROI recomputes30-12=18 and18/12*100=150%', roi?.revenue === 30 && roi.cost === 12 && roi.profit === 18 && roi.roiPercentage === 150, { roi });
        await p.goto('http://localhost:5173/analytics/attribution/roi', { waitUntil: 'domcontentloaded' });
        await p.getByText(roi.campaign, { exact: true }).first().waitFor();
        check('Actual ROI UI renders recorded campaign', true);
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally {
        if (cost?.campaignCostId) {
            const archived = await api('delete', '/admin/analytics/campaign-costs/' + cost.campaignCostId);
            checks.push({ name: 'Exact task cost archived; history retained', passed: archived.status === 200, costId: cost.campaignCostId });
        }
        const coupon = await api('get', '/coupons/' + s.couponId);
        if (coupon.status === 200 && coupon.data.code === s.marker) {
            const removed = await api('delete', '/coupons/' + s.couponId, undefined, coupon.data.rowVersion);
            const observed = await api('get', '/coupons/' + s.couponId);
            checks.push({ name: 'Used QA coupon safely archived and fresh GET404', passed: removed.status === 204 && observed.status === 404, usageBeforeArchive: coupon.data.currentUsage });
        }
        const cart = await page.evaluate(async cartId => {
            const { httpClient } = await import('/src/services/api/httpClient.ts');
            const before = await httpClient.get('/api/storefront/cart');
            if (before.data.cartId !== cartId || before.data.ownerType?.toLowerCase() !== 'customer' || before.data.items.length > 1) return { safe: false };
            await httpClient.delete('/api/storefront/cart');
            const after = await httpClient.get('/api/storefront/cart');
            return { safe: true, ownerType: after.data.ownerType, count: after.data.items.length, subtotal: after.data.subtotal };
        }, s.cartId);
        checks.push({ name: 'Exact customer basket cleaned with fresh independent readback', passed: cart.safe && cart.count === 0 && cart.subtotal === 0, cart });
        await page.evaluate(s => sessionStorage.setItem('phase8-scenario', JSON.stringify(s)), s);
    }
    return { observedUtc: new Date().toISOString(), marker: s.marker, orderId: s.orderId, cost, roi, checks, failures, externalAdSpend: 0 };
}

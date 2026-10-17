async page => {
  const pages = page.context().pages();
  const admin = pages.find(p => p.url().startsWith('http://localhost:5173/'));
  const portal = pages.find(p => p.url().startsWith('https://business.bosta.co/'));
  if (!admin || !portal) throw new Error('Expected existing authenticated tabs');
  const application = await admin.evaluate(async () => {
    const read = async path => {
      const r = await fetch('/api/' + path, { credentials: 'include' });
      return { http: r.status, body: await r.json() };
    };
    const accounts = await read('admin/shipping/carrier-accounts');
    const a = accounts.body.find(a => a.carrierCode === 'bosta');
    const order = await read('admin/orders/ac89be7f-5e8c-4834-bcec-a1d14d5af2e6');
    const o = order.body;
    return { accountsHttp: accounts.http, bosta: { id: a.id, environment: a.environment,
      state: a.state, operationalState: a.operationalState, savedCredentialFields: a.savedCredentialFields,
      connectionTestIsCurrent: a.connectionTestIsCurrent, lastConnectionTest: a.lastConnectionTest,
      integrationReadiness: a.integrationReadiness, serviceLevels: a.serviceLevels, warehouseAssignments: a.warehouseAssignments },
       order: { http: order.http, id: o.orderId, tenantId: o.tenantId,
         orderNumber: o.orderNumber, status: o.status, paymentStatus: o.paymentStatus, total: o.totals?.total,
        currency: o.currency, shippingCarrierCode: o.shippingCarrierCode, warehouseId: o.warehouseId,
        shippingMethodId: o.shippingMethodId, shipmentId: o.shipmentId,
        items: o.items?.map(i => ({ id: i.id, productId: i.productId, variantId: i.variantId, quantity: i.quantity })) } };
  });
  // Return metadata only: never customer/contact/address text or portal storage.
  const provider = await portal.evaluate(async () => {
    const stored = localStorage.getItem('newToken');
    let token = stored;
    try { const value = JSON.parse(stored); token = typeof value === 'string' ? value : value?.token; } catch { /* Raw token is supported. */ }
    if (!token) return { sessionTokenPresent: false };
    const r = await fetch('https://app.bosta.co/api/v2/pickup-locations', {
      headers: { Authorization: token.startsWith('Bearer ') ? token : 'Bearer ' + token }
    });
    let b; try { b = await r.json(); } catch { return { http: r.status, json: false }; }
    const headers = { Authorization: token.startsWith('Bearer ') ? token : 'Bearer ' + token };
    const profileResponse = await fetch('https://app.bosta.co/api/v2/businesses/z2A16HSAaJeMFg89BCwVE/profile', { headers });
    const profileBody = await profileResponse.json();
    const profile = profileBody.data || {};
    const shape = value => ({ type: Array.isArray(value) ? 'array' : value === null ? 'null' : typeof value,
      keys: value && typeof value === 'object' ? Object.keys(value).slice(0, 50) : [],
      sampleKeys: Array.isArray(value) && value[0] && typeof value[0] === 'object' ? Object.keys(value[0]) : [] });
    const flags = {};
    for (const key of ['isActive','autoCreatePickups','allowRecurringPickups','isNotValidatedByAccountManager',
      'isBankInfoSkipped','isLegalInfoSkipped','internationalShippingEnabled','validateFirstLine',
      'validateConsigneePhone','isBostaFulfillment','hasSufficientBostaCreditsToCreateOrder','isMandatoryItemValue','state','status']) {
      if (['boolean','number'].includes(typeof profile[key])) flags[key] = profile[key];
    }
    const pickup = b.data?.list?.find(l => l._id === 'PZ_CVuhF4R');
    let pickupCoverage = null;
    if (pickup?.address?.city?._id && pickup?.address?.district?._id) {
      const coverageResponse = await fetch(`https://app.bosta.co/api/v2/cities/${encodeURIComponent(pickup.address.city._id)}/districts`, { headers });
      const coverageBody = await coverageResponse.json();
      const rows = Array.isArray(coverageBody.data) ? coverageBody.data : coverageBody.data?.list || [];
      const matches = rows.filter(d => (d.districtId || d._id) === pickup.address.district._id);
      pickupCoverage = { http: coverageResponse.status, success: coverageBody.success, districtCount: rows.length,
        matches: matches.map(d => ({districtId:d.districtId || d._id, pickupAvailability:d.pickupAvailability,
          dropOffAvailability:d.dropOffAvailability, coverage:d.coverage})) };
    }
    return { http: r.status, success: b.success, total: b.data?.total, pickupCoverage,
      profile: { http: profileResponse.status, success: profileBody.success, flags,
        pricingShape: {priceList:shape(profile.priceList), pricingFlags:shape(profile.pricingFlags), cashOutFeesInfo:shape(profile.cashOutFeesInfo)} },
      locations: b.data?.list?.map(l => ({ id: l._id, isDefault: l.isDefault,
        country: l.address?.country, city: l.address?.city, zone: l.address?.zone, district: l.address?.district,
        streetPresent: typeof l.address?.firstLine === 'string' && l.address.firstLine.length > 5,
        contactPresent: !!l.contactPerson,
        keys: Object.keys(l), addressKeys: Object.keys(l.address || {}) })) };
  });
  return { observedUtc: new Date().toISOString(), application, provider, mutations: 0 };
}

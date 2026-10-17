async page => {
  const portal = page.context().pages().find(p => p.url().startsWith('https://business.bosta.co/'));
  if (!portal) throw new Error('Existing authenticated provider session required');
  return portal.evaluate(async () => {
    const stored = localStorage.getItem('newToken');
    let token = stored;
    try { const v = JSON.parse(stored); token = typeof v === 'string' ? v : v?.token; } catch { /* Existing raw session representation. */ }
    if (!token) return { sessionAvailable: false };
    const headers = { Authorization: token.startsWith('Bearer ') ? token : 'Bearer ' + token };
    const get = async route => {
      const r = await fetch('https://app.bosta.co/api/v2/' + route, { headers });
      const b = await r.json();
      return { http: r.status, success: b.success, data: b.data };
    };
    const pickups = await get('pickup-locations');
    const cities = await get('cities');
    const pickup = pickups.data?.list?.find(p => p._id === 'PZ_CVuhF4R');
    const districts = pickup?.address?.city?._id ? await get('cities/' + encodeURIComponent(pickup.address.city._id) + '/districts') : null;
    return { observedUtc: new Date().toISOString(), testOnlyReferenceSnapshot: true, mutations: 0,
      pickups: {http: pickups.http, success: pickups.success, locations: pickups.data?.list?.map(p => ({id:p._id,
        country:p.address?.country, city:p.address?.city, district:p.address?.district, zone:p.address?.zone}))},
      cities, districts };
  });
}

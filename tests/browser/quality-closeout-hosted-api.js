async page => {
    await page.goto('http://localhost:5173/');
    const evidence = await page.evaluate(async () => {
        const results = [];
        const check = (name, passed, evidence = {}) => { results.push({ name, passed, ...evidence }); };
        const request = async (path, options = {}) => {
            const response = await fetch('/api/' + path, { credentials: 'include', ...options });
            let data; try { data = await response.json(); } catch { data = null; }
            return { status: response.status, data };
        };
        const session = await request('auth/session');
        const user = session.data.user ?? session.data;
        check('hosted Owner session reads the new canonical attachment permission', session.status === 200 && user.role === 'Owner' && user.effectivePermissions.includes('storefront.orders.attachments.download'), { http: session.status });
        const csrf = decodeURIComponent(document.cookie.split(';').map(p => p.trim()).find(p => p.startsWith('csrf_token='))?.slice(11) || '');
        const id = crypto.randomUUID();
        const writes = { headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf }, body: '{}' };
        const probes = [
            ['admin/homepage-media/' + id + '/preview', {}],
            ['admin/orders/' + id + '/checkout-attachments/' + id + '/download', {}],
            ['admin/shipments/' + id + '/parcel-evidence', {}],
            ['admin/shipments/' + id + '/parcel-evidence', { ...writes, method: 'PUT' }],
            ['admin/shipments/' + id + '/parcel-quote', { ...writes, method: 'POST' }],
        ];
        for (const [path, options] of probes) {
            const response = await request(path, options);
            check('Owner reaches validation/unknown-resource boundary: ' + (options.method || 'GET') + ' ' + path.replaceAll(id, '<unknown-id>'), [400, 404].includes(response.status) || (response.status === 409 && response.data?.code === 'SHIPMENT_NOT_FOUND'), { http: response.status, code: response.data?.code ?? null });
        }
        const upload = await request('admin/homepage-media', { method: 'POST', headers: { 'X-CSRF-Token': csrf }, body: new FormData() });
        check('Owner upload without a file fails before storage mutation', upload.status === 400, { http: upload.status, code: upload.data?.code ?? null });
        const shipments = await request('admin/shipments?pageSize=1');
        const shipment = shipments.data?.items?.[0];
        if (shipment) {
            const parcel = await request(`admin/shipments/${shipment.id}/parcel-evidence`);
            check('Owner can read existing parcel evidence', parcel.status === 200, { http: parcel.status });
        }
        const whatsapp = await request('admin/whatsapp/accounts');
        check('existing WhatsApp account state is readable without activation', whatsapp.status === 200 && Array.isArray(whatsapp.data), { http: whatsapp.status, accounts: whatsapp.data?.map(a => ({ enabled: a.enabled, status: a.status, otp: a.useForOtpVerification })) });
        return { observedUtc: new Date().toISOString(), layer: 'actual-hosted-API', results, realMessagesSubmitted: 0, realBusinessMutations: 0 };
    });
    // The API deliberately ignores inbound X-Tenant-Id. Use the real verified
    // tenant host to reach authentication, with a fresh cookie-free context.
    const guest = await page.context().browser().newContext();
    try {
        const id = '11111111-1111-4111-8111-111111111111';
        for (const [method, path] of [
            ['GET', `admin/homepage-media/${id}/preview`], ['POST', 'admin/homepage-media'],
            ['GET', `admin/orders/${id}/checkout-attachments/${id}/download`],
            ['GET', `admin/shipments/${id}/parcel-evidence`], ['PUT', `admin/shipments/${id}/parcel-evidence`],
            ['POST', `admin/shipments/${id}/parcel-quote`],
        ]) {
            const response = await guest.request.fetch('http://localhost:5010/api/' + path, { method, headers: { Host: 'ahramstore.localhost:5174' } });
            evidence.results.push({ name: `verified tenant host guest is unauthorized: ${method} ${path.replaceAll(id, '<unknown-id>')}`, passed: response.status() === 401, http: response.status() });
        }
    } finally { await guest.close(); }
    return evidence;
}

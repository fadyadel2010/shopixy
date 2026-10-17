async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    if (!admin) throw new Error('Existing authenticated Admin tab required');
    const writes = [];
    const observe = request => {
        if (request.url().includes('/api/admin/shipping/') && !['GET', 'HEAD', 'OPTIONS'].includes(request.method()))
            writes.push({ method: request.method(), path: request.url().split('?')[0].split('/api/')[1] });
    };
    admin.on('request', observe);
    try {
        await admin.goto('http://localhost:5173/admin/shipping', { waitUntil: 'domcontentloaded' });
        await admin.getByRole('tab').nth(1).click();
        const card = admin.locator('article').filter({ has: admin.locator('bdi').filter({ hasText: /^bosta$/ }) });
        const load = card.getByRole('button', { name: /Load saved-account pickup locations|تحميل مواقع الاستلام/ });
        await load.waitFor({ state: 'visible' });
        const enabled = await load.isEnabled();
        if (!enabled) return { observedUtc: new Date().toISOString(), loadEnabled: false, shippingWrites: writes };
        const response = admin.waitForResponse(r => r.url().includes('/bosta/pickup-locations') && r.request().method() === 'GET');
        await load.click();
        const http = await response;
        const select = card.getByRole('combobox', { name: /Choose verified pickup location|اختر موقع الاستلام المؤكد/ });
        await select.waitFor({ state: 'visible' });
        return { observedUtc: new Date().toISOString(), loadEnabled: enabled, discoveryHttp: http.status(),
            selectedPickup: await select.inputValue(), locationIds: await select.locator('option').evaluateAll(options => options.map(option => option.value).filter(Boolean)),
            manuallyEnteredLocationId: await card.locator('input[name="ExtraConfig.businessLocationId"]').inputValue(),
            shippingWrites: writes, savedConfiguration: false, liveShipmentCreated: false, pickupRequested: false };
    } finally { admin.off('request', observe); }
}

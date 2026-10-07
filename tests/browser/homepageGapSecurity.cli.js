async page => {
    const last = await page.evaluate(() => JSON.parse(sessionStorage.getItem('gap-preview-results')).at(-1));
    const context = await page.context().browser().newContext();
    const guest = await context.newPage();
    try {
        await guest.goto('http://ahramstore.localhost:5174/');
        const anonymous = await guest.evaluate(async asset => {
            const live = await (await fetch('/api/storefront/store-config')).json();
            return { preview: (await fetch('/api/storefront/config/preview')).status,
                asset: (await fetch(asset)).status, publicContainsDraftChrome: !!live.previewChrome,
                image: live.pages.home.sections[0].settings.backgroundImage, theme: live.storefrontTheme };
        }, last.asset);
        if (anonymous.preview !== 401 || anonymous.asset !== 404 || anonymous.publicContainsDraftChrome) throw new Error('Anonymous draft boundary failed');
        const signed = await page.evaluate(async () => (await (await import('/src/modules/storefront/services/storeConfigService.js')).storeConfigService.createThemePreviewLink('fashion')));
        const anonymousSignedDraft = await guest.evaluate(async token => (await fetch(`/api/storefront/config/preview?draftThemePreviewToken=${encodeURIComponent(token)}`)).status, signed.previewToken);
        if (anonymousSignedDraft !== 401) throw new Error('Public theme capability authorized a private Admin draft');
        await guest.goto(`http://ahramstore.localhost:5174/draft-preview.html?tenant=${last.publicBoundary.tenant || 'c9c5c1ae-949e-498e-bc63-07242daeb49d'}#anonymous-nonce`);
        await guest.waitForTimeout(400);
        const standalone = { headers: await guest.locator('header').count(), status: await guest.getByRole('status').innerText() };
        if (standalone.headers !== 0) throw new Error('Standalone renderer obtained draft data');
        const forged = await page.evaluate(async () => ({
            invalidToken: (await fetch('/api/storefront/config/preview?draftThemePreviewToken=invalid-token')).status,
            conflictingTenant: (await fetch('/api/storefront/config/preview', { headers: { 'X-Tenant-Id': 'c5372303-d2eb-4c77-a543-4b84b8e76d3c' } })).status,
        }));
        if (forged.invalidToken !== 401 || forged.conflictingTenant !== 403) throw new Error(`Invalid authority was accepted ${JSON.stringify(forged)}`);
        const evidence = { anonymous, anonymousSignedDraft, standalone, forged, tenantIsolationBackendCovered: true };
        await page.evaluate(evidence => sessionStorage.setItem('gap-security-evidence', JSON.stringify(evidence)), evidence);
        return evidence;
    } finally { await context.close(); }
}

async page => {
    const root = '.ai/evidence/smart-offers-20261020';
    const p = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    if (!p) throw new Error('The authenticated AhramStore tab must already exist.');
    const results = [];
    const check = (value, message) => { if (!value) throw new Error(message); results.push(message); };
    const actual = await p.evaluate(async () => {
        const { fetchSmartOffers } = await import('/src/services/api/storefrontApi.ts');
        const { getEligibleOffers, loadSmartOffers } = await import('/src/storefront/services/smartOfferEngine.ts');
        const publicOffers = await fetchSmartOffers();
        const loaded = await loadSmartOffers(true);
        return { hostname: location.hostname, publicOffers, eligibleOnHome: getEligibleOffers({ offers: loaded, pageType: 'Home' }) };
    });
    check(actual.hostname === 'ahramstore.localhost', 'Storefront verification uses AhramStore, not Nissan');
    check(!actual.publicOffers.some(offer => offer.id === '74f9a885-63ad-4902-9250-73d1bec97743'), 'Real Storefront API excludes the persisted inactive non-coupon fixture');
    check(actual.publicOffers.every(offer => !('couponCode' in offer) && !('couponDiscountValue' in offer)), 'Public API exposes presentation fields, not private coupon configuration');
    await p.screenshot({ path: `${root}/storefront-real-inactive-excluded.png` });

    // Render the real Storefront component with an isolated public-DTO fixture.
    // Never activate the retained SQL fixture or redeem any coupon.
    const context = await p.context().browser().newContext({ viewport: { width: 1440, height: 1000 }, storageState: await p.context().storageState() });
    const fixturePage = await context.newPage();
    const errors = [];
    const failures = [];
    fixturePage.on('pageerror', error => errors.push(error.message));
    fixturePage.on('response', response => { if (response.url().includes('/api/') && response.status() >= 400) failures.push({ url: response.url(), status: response.status() }); });
    const fixture = { id: '00000000-0000-4000-8000-000000000020', name: 'Isolated UI fixture', priority: 12, triggerType: 'TimeDelay',
        pagesScope: 'Home', delaySeconds: 1, scrollPercent: 45, showOncePerSession: true, maxDisplaysPerUser: 3,
        popupTitle: 'عرض تجريبي — Preview', popupDescription: null, buttonText: 'Continue — متابعة', backgroundColor: '#f97316', imageUrl: null };
    await fixturePage.route('**/api/storefront/smart-offers*', route => route.request().method() === 'GET'
        ? route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([fixture]) })
        : route.abort('blockedbyclient'));
    await fixturePage.goto('http://ahramstore.localhost:5174/', { waitUntil: 'domcontentloaded', timeout: 60000 });
    const popup = fixturePage.getByRole('dialog', { name: fixture.popupTitle });
    await popup.waitFor({ timeout: 60000 });
    check(await popup.getByRole('button', { name: fixture.buttonText, exact: true }).count() === 1, 'Real Storefront popup renders configured title and button');
    check(await popup.getByText('Server-verified offer', { exact: true }).count() === 1, 'Real popup presents the same server-verified offer semantics as Admin preview');
    check(await popup.locator('p').count() === 0, 'No description is fabricated when the public DTO has none');
    await fixturePage.screenshot({ path: `${root}/storefront-isolated-popup.png` });
    await popup.getByRole('button', { name: 'Close offer', exact: true }).click();
    await fixturePage.reload({ waitUntil: 'domcontentloaded', timeout: 60000 });
    await fixturePage.locator('main').waitFor();
    const eligibility = await fixturePage.evaluate(async () => {
        const { getEligibleOffers } = await import('/src/storefront/services/smartOfferEngine.ts');
        const { loadSmartOffers } = await import('/src/storefront/services/smartOfferEngine.ts');
        return getEligibleOffers({ offers: await loadSmartOffers(), pageType: 'Home', elapsedSeconds: 999 }).map(offer => offer.id);
    });
    check(!eligibility.includes(fixture.id), 'Once-per-session configuration excludes the displayed fixture after reload');
    await context.close();
    return { results, actual, isolatedFixture: fixture, errors, failures,
        scope: 'Real inactive API filtering plus isolated public-DTO rendering only; no live activation, redemption, cart, or order mutation.' };
}

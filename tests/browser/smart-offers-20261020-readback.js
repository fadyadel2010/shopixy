async page => {
    // Continue the one already-created safe fixture after an external resource
    // delayed the original navigation load event. Do not create another record.
    const root = '.ai/evidence/smart-offers-20261020';
    const results = [];
    const check = (value, message) => { if (!value) throw new Error(message); results.push(message); };
    const row = page.getByRole('row').filter({ hasText: 'QA Smart Offers UI 20261020 18bc094d' });
    const dialog = page.getByRole('dialog');
    const field = label => dialog.getByLabel(label, { exact: true });
    await row.getByRole('button', { name: 'Edit', exact: true }).click();
    check(await field('Priority').inputValue() === '0' && await field('Scroll percent').inputValue() === '55', 'Persisted zero and updated depth survive full navigation');
    check(await field('Max displays per user').inputValue() === '' && await field('Description').inputValue() === '', 'Nullable values reopen empty without fabricated defaults');
    check(await field('Trigger type').inputValue() === 'ScrollDepth' && await field('Pages scope').inputValue() === 'Cart', 'Persisted trigger and scope survive full navigation');
    check(await field('Status').inputValue() === 'Inactive' && await field('Coupon code').inputValue() === '', 'Safe fixture remains inactive and non-redeemable');
    await page.screenshot({ path: `${root}/after-navigation-reopened.png` });
    await dialog.getByRole('button', { name: 'Close editor' }).click();
    await row.getByRole('button', { name: 'Details', exact: true }).click();
    await dialog.getByRole('heading', { name: /QA Smart Offers UI/ }).waitFor();
    await page.screenshot({ path: `${root}/after-details.png` });
    const details = await dialog.innerText();
    await dialog.getByRole('button', { name: 'Close', exact: true }).click();
    const persisted = await page.evaluate(async () => {
        const { getAdminSmartOffer } = await import('/src/services/adminSmartOffersApi.js');
        const { useAuthStore } = await import('/src/store/authStore.js');
        return getAdminSmartOffer('74f9a885-63ad-4902-9250-73d1bec97743', useAuthStore.getState().user);
    });
    check(Number(persisted.rowVersion) > 0 && persisted.status === 'Inactive' && !persisted.generatedCouponId, 'Admitted API readback confirms a backend row version and no linked coupon');
    return { results, details, persisted, retention: 'Retained inactive fixture; no deletion, activation, or coupon generated.' };
}

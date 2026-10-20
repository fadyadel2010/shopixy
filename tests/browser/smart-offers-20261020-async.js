async page => {
    const root = '.ai/evidence/smart-offers-20261020';
    const p = await page.context().newPage();
    const results = [];
    const check = (value, message) => { if (!value) throw new Error(message); results.push(message); };
    let release;
    const pending = new Promise(resolve => { release = resolve; });
    let failReads = false;
    await p.route('**/api/admin/smart-offers**', async route => {
        if (route.request().method() !== 'GET') {
            await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'QA simulated save failure — no server mutation' }) });
            return;
        }
        await pending;
        if (failReads) await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'QA simulated read failure' }) });
        else await route.continue();
    });
    await p.goto('http://localhost:5173/ecommerce/smart-offers', { waitUntil: 'commit', timeout: 60000 });
    await p.getByRole('button', { name: 'Create offer', exact: true }).click({ timeout: 60000 });
    const dialog = p.getByRole('dialog');
    await dialog.getByLabel('Name', { exact: true }).fill('Pending-load draft');
    await dialog.getByLabel('Title', { exact: true }).fill('Pending preview title');
    await dialog.getByLabel('Button text', { exact: true }).fill('Continue');
    await dialog.getByLabel('Priority', { exact: true }).fill('24');
    await p.screenshot({ path: `${root}/after-during-loading.png` });
    release();
    await p.locator('tbody tr').filter({ hasText: 'QA Smart Offers UI 20261020 18bc094d' }).waitFor({ state: 'attached' });
    check(await dialog.getByLabel('Priority', { exact: true }).inputValue() === '24', 'Late list and stats responses do not overwrite open draft');
    check(await dialog.getByRole('heading', { name: 'Pending preview title' }).count() === 1, 'Preview retained after asynchronous loading');
    await p.screenshot({ path: `${root}/after-post-loading.png` });
    await dialog.getByRole('button', { name: 'Create offer', exact: true }).click();
    await dialog.getByRole('alert').waitFor();
    check(await dialog.getByLabel('Name', { exact: true }).inputValue() === 'Pending-load draft', 'Simulated failed save retains draft');
    await p.screenshot({ path: `${root}/after-save-error.png` });
    await dialog.getByRole('button', { name: 'Close editor' }).click();
    failReads = true;
    await p.getByRole('button', { name: 'Refresh', exact: true }).click();
    await p.getByRole('alert').waitFor();
    check(await p.getByText('No smart offers yet', { exact: true }).count() === 0, 'API read failure is not presented as empty data');
    await p.getByText('Offer statistics are unavailable. Refresh to retry.').waitFor();
    await p.screenshot({ path: `${root}/after-read-error.png` });
    failReads = false;
    await p.getByRole('button', { name: 'Retry', exact: true }).click();
    await p.getByRole('row').filter({ hasText: 'QA Smart Offers UI 20261020 18bc094d' }).waitFor();
    check(await p.getByRole('alert').count() === 0, 'Retry restores real API data');
    await p.close();
    return { results, network: '503 failures intentionally intercepted in an extra tab; no failing mutations reached API.' };
}

async page => {
    const root = '.ai/evidence/smart-offers-20261020';
    const results = [];
    const check = (value, message) => { if (!value) throw new Error(message); results.push(message); };
    for (const width of [1440, 1280, 768, 390]) {
        await page.setViewportSize({ width, height: 1000 });
        await page.getByRole('heading', { name: 'Smart offers', exact: true }).scrollIntoViewIfNeeded();
        const layout = await page.evaluate(() => ({ width: innerWidth, scrollWidth: document.documentElement.scrollWidth }));
        check(layout.scrollWidth <= layout.width, `Loaded offer table stays in its own scroll container at ${width}px`);
        await page.screenshot({ path: `${root}/after-table-${width}.png` });
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.getByRole('button', { name: 'Create offer', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Title', { exact: true }).fill('Optional content preview');
    await dialog.getByLabel('Description', { exact: true }).fill('Current description — وصف');
    await dialog.getByLabel('Background color', { exact: true }).fill('#2563eb');
    const artwork = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="400" height="160"><rect width="400" height="160" fill="#dbeafe"/><text x="20" y="90" font-size="28">QA preview artwork</text></svg>');
    await dialog.getByLabel('Image URL', { exact: true }).fill(artwork);
    const image = dialog.getByRole('img', { name: 'Optional content preview' });
    await image.waitFor();
    check(await image.getAttribute('src') === artwork, 'Artwork preview uses the latest form URL');
    check(await dialog.getByLabel('Choose color', { exact: true }).inputValue() === '#2563eb', 'Text and color controls remain synchronized');
    await dialog.getByLabel('Name', { exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: `${root}/after-content-preview.png` });
    await dialog.getByLabel('Description', { exact: true }).fill('');
    await dialog.getByLabel('Image URL', { exact: true }).fill('');
    check(await dialog.getByRole('img').count() === 0 && await dialog.getByText('Current description — وصف', { exact: true }).count() === 0, 'Clearing optional content removes it without stale artwork or invented description');
    await dialog.getByRole('button', { name: 'Close editor' }).click();
    return { results, persistence: 'No save performed; optional artwork/content checked only in a discarded draft.' };
}

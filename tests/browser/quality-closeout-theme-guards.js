async page => {
    const tab = await page.context().newPage();
    const results = [];
    let saves = 0, publications = 0;
    const check = (name, passed) => { results.push({ name, passed }); if (!passed) throw new Error(name); };
    const failSave = async route => { saves++; await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Closeout controlled rejection' }) }); };
    const noPublish = async route => { publications++; await route.abort(); };
    try {
        await tab.goto('http://localhost:5173/storefront/appearance/colors-typography');
        const primary = tab.getByLabel('Primary Color', { exact: true });
        await primary.waitFor();
        const before = await (await tab.request.get('http://localhost:5173/api/admin/store-config')).json();
        await tab.route('**/api/admin/store-config/theme/overrides', failSave);
        await tab.route('**/api/admin/store-config/theme/publish', noPublish);
        await primary.fill('invalid');
        await tab.evaluate(() => document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true, bubbles: true, cancelable: true })));
        check('invalid keyboard save makes no request', saves === 0);
        check('invalid candidate cannot publish', await tab.getByRole('button', { name: 'Publish Colors & Typography', exact: true }).isDisabled());
        await tab.getByRole('button', { name: 'Discard', exact: true }).click();
        await primary.fill('#123456');
        await tab.getByRole('button', { name: 'Publish Colors & Typography', exact: true }).click();
        await tab.getByRole('dialog').getByRole('button', { name: 'Publish Colors & Typography', exact: true }).click();
        await tab.getByText('Closeout controlled rejection', { exact: true }).waitFor();
        check('publication awaits draft acceptance and does not escape failed save', saves === 1 && publications === 0);
        check('failed draft retains user candidate for retry', await primary.inputValue() === '#123456');
        await tab.getByRole('dialog').getByRole('button', { name: 'Keep editing', exact: true }).click();
        await tab.getByRole('button', { name: 'Discard', exact: true }).click();
        const after = await (await tab.request.get('http://localhost:5173/api/admin/store-config')).json();
        check('hosted draft/live/revision remain unchanged', after.rowVersion === before.rowVersion && after.themeDraftOverridesJson === before.themeDraftOverridesJson && after.themeLiveOverridesJson === before.themeLiveOverridesJson);
        return { layer: 'hosted-editor-controlled-failure', results, realMutations: 0, interceptedSaves: saves, publications };
    } finally {
        await tab.unroute('**/api/admin/store-config/theme/overrides', failSave);
        await tab.unroute('**/api/admin/store-config/theme/publish', noPublish);
        await tab.close();
    }
}

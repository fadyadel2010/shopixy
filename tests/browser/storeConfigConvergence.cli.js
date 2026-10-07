// Invoke once per checkpoint, interleaving StoreConfigTrace --wait --checkpoint A/B/C/Rollback.
async page => {
    const handleDialog = async dialog => { if (dialog.type() === 'beforeunload') await dialog.accept(); };
    page.on('dialog', handleDialog);
    try {
    page.setDefaultTimeout(20000);
    const read = async path => page.evaluate(async path => (await (await import('/src/services/api.js')).default.get(path)).data, path);
    const stage = await page.evaluate(() => sessionStorage.getItem('gap-convergence-stage') || 'A');
    const live = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/')) || await page.context().newPage();
    const clean = () => page.waitForFunction(async () => !(await import('/src/modules/storefront/store/index.js')).useBuilderStore.getState().isDirty);
    const response = (path, method) => page.waitForResponse(r => r.url().endsWith(path) && r.request().method() === method);
    const accept = async promise => { const r = await promise; if (r.status() !== 200) throw new Error(`${r.status()} ${await r.text()}`); return r.json(); };
    const publishedHome = async () => {
        await page.getByRole('button', { name: 'Publish Homepage', exact: true }).click();
        const result = response('/admin/store-config/pages/home/publish', 'POST');
        await page.getByRole('dialog').getByRole('button', { name: 'Publish Homepage', exact: true }).click();
        const config = await accept(result); await page.getByRole('dialog').waitFor({ state: 'hidden' }); return config;
    };
    await page.setViewportSize({ width: 1600, height: 1000 });
    await page.goto('http://localhost:5173/storefront/homepage');
    await page.getByRole('button', { name: /^(Edit|تعديل)$/ }).first().waitFor();
    if ((await page.locator('html').getAttribute('lang')) !== 'en') await page.getByRole('button', { name: 'EN', exact: true }).click();
    let evidence = await page.evaluate(() => JSON.parse(sessionStorage.getItem('gap-convergence-evidence') || '{}'));
    if (stage === 'A' || stage === 'B') {
        if (stage === 'A' && !evidence.original) evidence.original = { config: await read('/admin/store-config'), navigation: await read('/admin/cms/navigation') };
        await page.getByRole('button', { name: 'Edit', exact: true }).first().click();
        const uploaded = response('/admin/homepage-media', 'POST');
        await page.getByLabel('Background Image', { exact: true }).setInputFiles(stage === 'A' ? '.artifacts/media-cert-primary.png' : '.artifacts/media-cert-b.png');
        const asset = await accept(uploaded);
        await page.getByRole('img', { name: 'Image preview', exact: true }).evaluate(i => i.decode());
        await page.getByRole('main').getByRole('textbox').first().fill(`CERT Convergence ${stage}`);
        const saved = response('/admin/store-config/pages/home', 'PUT');
        await page.getByRole('button', { name: 'Save Draft', exact: true }).click(); await accept(saved); await clean();
        await live.goto('http://ahramstore.localhost:5174/');
        const draftStatus = await live.evaluate(async ref => (await fetch(ref, { cache: 'no-store' })).status, asset.reference);
        if (draftStatus !== 404) throw new Error('Draft media leaked before publish');
        const published = await publishedHome();
        const versions = await read('/admin/store-config/versions');
        evidence[stage] = { asset: asset.reference, version: versions.versions.find(v => v.isCurrent).version, tenant: published.tenantId, draftStatus };
    } else if (stage === 'C') {
        await page.goto('http://localhost:5173/storefront/navigation?view=header');
        await page.getByLabel('Announcement text (English)', { exact: true }).fill('CERT Convergence C Header');
        await page.getByLabel('Announcement text (Arabic)', { exact: true }).fill('إعلان اعتماد التقارب');
        await page.getByLabel('Show announcement bar', { exact: true }).check();
        await page.getByRole('button', { name: 'Publish Header and Footer Navigation', exact: true }).click();
        const result = response('/admin/cms/navigation/publish', 'POST');
        await page.getByRole('dialog').getByRole('button', { name: 'Publish Header and Footer Navigation', exact: true }).click();
        await accept(result); await page.getByRole('dialog').waitFor({ state: 'hidden' });
        evidence.C = { version: (await read('/admin/store-config/versions')).versions.find(v => v.isCurrent).version };
    } else if (stage === 'Rollback') {
        await page.goto('http://localhost:5173/storefront/storeconfig-version-history');
        await page.getByRole('heading', { name: `Version ID v${evidence.A.version}`, exact: true }).locator('xpath=../../../..').getByRole('button', { name: 'Rollback', exact: true }).click();
        const dialog = page.getByRole('dialog'); await dialog.getByRole('checkbox').check();
        await dialog.getByRole('textbox').fill('Controlled SQL-event-consumer-Mongo rollback convergence certification');
        const result = response(`/admin/store-config/rollback/${evidence.A.version}`, 'POST');
        await dialog.getByRole('button', { name: 'Rollback Now', exact: true }).click(); await accept(result); await dialog.waitFor({ state: 'hidden' });
        evidence.Rollback = { restoredFrom: evidence.A.version, version: (await read('/admin/store-config/versions')).versions.find(v => v.isCurrent).version };
    } else return { done: true, evidence };
    await live.goto('http://ahramstore.localhost:5174/');
    const expected = stage === 'A' || stage === 'Rollback' ? evidence.A.asset : evidence.B.asset;
    await live.waitForFunction(async expected => (await import('/src/store/storeConfigStore.ts')).useStoreConfigStore.getState().config.pages?.home?.sections[0]?.settings.backgroundImage === expected, expected);
    await live.getByRole('heading', { name: `CERT Convergence ${stage === 'A' || stage === 'Rollback' ? 'A' : 'B'}`, exact: false }).waitFor();
    if (stage === 'C') await live.getByRole('region', { name: 'Store announcement', exact: true }).filter({ hasText: 'CERT Convergence C Header' }).waitFor();
    await live.waitForTimeout(500);
    await live.screenshot({ path: `.artifacts/gap-convergence-${stage}-storefront.png` });
    await live.reload();
    await live.waitForFunction(async expected => (await import('/src/store/storeConfigStore.ts')).useStoreConfigStore.getState().config.pages?.home?.sections[0]?.settings.backgroundImage === expected, expected);
    const next = { A: 'B', B: 'C', C: 'Rollback', Rollback: 'done' }[stage];
    await page.evaluate(({ evidence, next }) => { sessionStorage.setItem('gap-convergence-evidence', JSON.stringify(evidence)); sessionStorage.setItem('gap-convergence-stage', next); }, { evidence, next });
    return { stage, result: evidence[stage], reloadPersisted: true };
    } finally { page.off('dialog', handleDialog); }
}

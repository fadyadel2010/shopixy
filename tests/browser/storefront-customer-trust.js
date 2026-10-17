// Uses only the previously recorded Phase 4 QA phone/order. No checkout or spam
// report is committed. Supported unblock restores the sending-independent gate.
async page => {
    const id = 'ee42f429-fa3d-46b6-988a-b900cc3e8648', phone = '+201000000099';
    const checks = [], failures = [], requests = [], errors = [], screenshots = [];
    const check = (name, passed, detail = {}) => { checks.push({ name, passed: !!passed, ...detail }); if (!passed) throw new Error(name); };
    const onError = e => errors.push(e.message);
    const onResponse = r => { if (/\/api\/admin\/(phones|orders\/[^/]+\/customer-(trust|phone))/.test(r.url())) requests.push({ path: r.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0], method: r.request().method(), status: r.status() }); };
    page.on('pageerror', onError); page.on('response', onResponse);
    const api = (method, path, data) => page.evaluate(async ({ method, path, data }) => {
        const api = (await import('/src/services/api.js')).default;
        const r = await api.request({ method, url: path, data }); return { status: r.status, data: r.data };
    }, { method, path, data });
    const readTrust = async () => (await api('get', '/admin/orders/' + id + '/customer-trust')).data;
    const words = {
        en: { title: 'Customer trust evidence', safe: 'Safe', block: 'Block Phone', unblock: 'Unblock Phone', listTitle: 'Blocked Phones', manage: 'Manage blocked phones', listUnblock: 'Unblock', cancel: 'Cancel', reported: 'Report Spam' },
        ar: { title: 'أدلة موثوقية العميل', safe: 'آمن', block: 'حظر الرقم', unblock: 'إلغاء حظر الرقم', listTitle: 'أرقام الهواتف المحظورة', manage: 'إدارة الأرقام المحظورة', listUnblock: 'إلغاء الحظر', cancel: 'إلغاء', reported: 'الإبلاغ كرسائل مزعجة' },
    };
    let ownedBlock = false;
    const viewport = page.viewportSize();
    const setLanguage = async (language, reopen) => {
        const button = page.getByRole('button', { name: language === 'ar' ? 'AR' : 'EN', exact: true });
        if (!await button.count()) return;
        const close = page.locator('aside[role=dialog] button[aria-label]').first();
        if (await close.count()) await close.click();
        await button.click();
        if (reopen) await page.getByText('ORD-C9C5-2026-01054', { exact: true }).first().click();
    };
    try {
        await page.goto('http://localhost:5173/ecommerce/orders/' + id);
        await page.locator('main h1').waitFor();
        const initial = await readTrust();
        check('Recorded fixture is unblocked and resolves the expected QA identity before any action', initial.phoneNumber === phone && !initial.phoneRisk?.isBlocked);
        for (const lang of ['en', 'ar']) {
            const w = words[lang];
            await setLanguage(lang, true);
            const section = page.locator('section').filter({ has: page.getByText(w.title, { exact: true }) });
            await section.getByRole('button', { name: w.block, exact: true }).waitFor();
            check(lang + ': initial heuristic Safe is shown only for an unblocked usable identity', await section.getByText(w.safe, { exact: true }).isVisible());
            check(lang + ': the report action is available without implicitly reporting or blocking', await section.getByRole('button', { name: w.reported, exact: true }).isEnabled());
            await section.getByRole('button', { name: w.block, exact: true }).click();
            await section.getByRole('button', { name: w.unblock, exact: true }).waitFor(); ownedBlock = true;
            const blocked = await readTrust();
            check(lang + ': order-scoped block persists on the expected canonical phone', blocked.phoneNumber === phone && blocked.phoneRisk.isBlocked);
            check(lang + ': a blocked phone is never labelled Safe', await section.getByText(w.safe, { exact: true }).count() === 0);
            await section.getByRole('link', { name: w.manage, exact: true }).click();
            await page.waitForURL('**/ecommerce/blocked-phones?**');
            await page.locator('main').getByRole('button', { name: w.listUnblock, exact: true }).waitFor();
            check(lang + ': trust link carries the exact phone into blocked-list search', await page.evaluate(() => new URL(location.href).searchParams.get('search')) === phone && await page.getByRole('textbox').inputValue() === phone);
            check(lang + ': blocked-list heading is localized', (await page.locator('main h1').innerText()) === w.listTitle);
            check(lang + ': phone presentation isolates its plus/country code left-to-right', await page.locator('main bdi[dir=ltr]').innerText() === phone);
            if (lang === 'ar') {
                check('Arabic populated table headers are translated', await page.getByRole('columnheader', { name: 'رقم الهاتف', exact: true }).isVisible() && await page.getByRole('columnheader', { name: 'تاريخ الحظر', exact: true }).isVisible());
                await page.setViewportSize({ width: 390, height: 900 });
                await page.waitForTimeout(450); // Allow the layout's 300ms resize transition to settle.
                const path = '.ai/evidence/storefront-trust-20261017/blocked-phones-ar-mobile.png'; await page.screenshot({ path }); screenshots.push(path);
                check('Arabic mobile blocked-list page has bounded document width', await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
                if (viewport) await page.setViewportSize(viewport);
            }
            await page.locator('main').getByRole('button', { name: w.listUnblock, exact: true }).click();
            await page.getByRole('dialog').getByRole('button', { name: w.cancel, exact: true }).click();
            check(lang + ': cancelling unblock keeps the phone blocked', (await readTrust()).phoneRisk.isBlocked);
            await page.locator('main').getByRole('button', { name: w.listUnblock, exact: true }).click();
            await page.getByRole('dialog').getByRole('button', { name: w.listUnblock, exact: true }).click();
            await page.locator('main').getByRole('button', { name: w.listUnblock, exact: true }).waitFor({ state: 'detached' });
            check(lang + ': list unblock has authoritative fresh readback', !(await readTrust()).phoneRisk.isBlocked);
            ownedBlock = false;
            // SPA history, not reload/goto: this exercises the retained QueryClient.
            await page.goBack();
            await section.getByRole('button', { name: w.block, exact: true }).waitFor();
            check(lang + ': returning through SPA history does not retain stale blocked trust', await section.getByText(w.safe, { exact: true }).isVisible());
        }
        const final = await readTrust();
        check('Final phone and report state restore the initial enforcement state', !final.phoneRisk.isBlocked && final.hasReportedSpam === initial.hasReportedSpam && final.phoneRisk.spamReports === initial.phoneRisk.spamReports && final.phoneRisk.globalSpamReports === initial.phoneRisk.globalSpamReports);
        check('No spam report was submitted by this hosted pass', !requests.some(x => x.method === 'POST' && x.path.endsWith('/report-spam')));
        check('No uncaught page exceptions during trust workflows', errors.length === 0);
    } catch (e) { failures.push(e.message.split('\n')[0]); }
    finally {
        if (ownedBlock) await api('post', '/admin/phones/unblock', { phoneNumber: phone });
        page.off('pageerror', onError); page.off('response', onResponse);
        if (viewport) await page.setViewportSize(viewport);
        await setLanguage('en', false);
    }
    return { orderId: id, phone, checks, failures, requests, errors, screenshots };
}

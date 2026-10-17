// Run with playwright-cli -s=opencode-ui-test run-code --filename=tests/browser/main-cod-ledger-certification.js
// Requires an already authenticated Owner session on localhost:5173.
// Set the language with the normal UI toggle and viewport with `resize` first.
// Read-only UI certification: no financial submission or merchant configuration writes.
async page => {
    const root = 'http://localhost:5173';
    const lang = await page.evaluate(() => document.documentElement.lang.startsWith('ar') ? 'ar' : 'en');
    const mobile = page.viewportSize().width === 390;
    const tag = `${lang}-${mobile ? 'mobile' : 'desktop'}`;
    const dir = lang === 'ar' ? 'rtl' : 'ltr';
    const evidence = [];
    const assert = (value, message) => { if (!value) throw new Error(message); };
    const checkPage = async (stage) => {
        const dimensions = await page.evaluate(() => ({
            lang: document.documentElement.lang,
            dir: document.documentElement.dir,
            width: document.documentElement.clientWidth,
            scrollWidth: document.documentElement.scrollWidth,
            mainDir: document.querySelector('main [dir]')?.getAttribute('dir'),
        }));
        assert(dimensions.dir === dir && dimensions.mainDir === dir, `${stage}: direction mismatch`);
        assert(dimensions.scrollWidth <= dimensions.width + 1, `${stage}: page-level horizontal overflow`);
        evidence.push({stage, ...dimensions, url:page.url()});
    };

    await page.goto(`${root}/treasury/hub`);
    const codCard = page.locator('main section[aria-labelledby="treasury-transit-heading"] button').filter({hasText:'COD Clearing'});
    await codCard.waitFor({state:'visible'});
    assert(await codCard.count() === 1, 'COD Clearing missing from in-transit section');
    assert(await codCard.innerText().then(text=>text.includes('0.00') || text.includes('٠٫٠٠') || text.includes('٠.٠٠')), 'Unexpected COD balance');
    await page.locator('main').getByText(lang === 'ar' ? /تسوية الدفع عند الاستلام وبوابات الدفع غير متاحة/ : /COD and gateway clearing settlement is currently unavailable/).waitFor();
    await checkPage('treasury-hub');
    await page.screenshot({path:`.artifacts/cod-ledger-audit/${tag}-hub.png`, fullPage:true});
    await codCard.click();
    await page.locator('main h2').filter({hasText:'COD Clearing'}).waitFor();
    assert(page.url().endsWith('/treasury/account/1130'), 'COD account navigation');
    await page.locator('main').getByText(lang === 'ar' ? /تسوية الدفع عند الاستلام وبوابات الدفع غير متاحة/ : /COD and gateway clearing settlement is currently unavailable/).waitFor();
    await checkPage('cod-account');

    await page.goto(`${root}/treasury/transfers`);
    await page.locator('#treasury-transfer-from option[value="1010"]').waitFor({state:'attached'});
    const sources = await page.locator('#treasury-transfer-from option').evaluateAll(options=>options.map(option=>({value:option.value,text:option.textContent})));
    const destinations = await page.locator('#treasury-transfer-to option').evaluateAll(options=>options.map(option=>({value:option.value,text:option.textContent})));
    assert(JSON.stringify(sources.filter(o=>o.value).map(o=>o.value).sort()) === JSON.stringify(['1010','1030']), 'Transfer source authority mismatch');
    assert(JSON.stringify(destinations.filter(o=>o.value).map(o=>o.value).sort()) === JSON.stringify(['1010','1030']), 'Transfer destination authority mismatch');
    evidence.push({stage:'transfer-options',sources,destinations});
    await checkPage('transfers');
    await page.screenshot({path:`.artifacts/cod-ledger-audit/${tag}-transfers.png`, fullPage:true});

    await page.goto(`${root}/treasury/clearing-aging`);
    await page.locator('main h2').waitFor();
    await page.locator('main').getByText(lang === 'ar' ? /تسوية الدفع عند الاستلام وبوابات الدفع غير متاحة/ : /COD and gateway clearing settlement is currently unavailable/).waitFor();
    await checkPage('clearing-aging');

    await page.goto(`${root}/treasury/general-ledger`);
    const selector = page.locator('#treasury-general-ledger-account');
    await selector.locator('option').filter({hasText:'1130 - COD Clearing'}).waitFor({state:'attached'});
    assert(await selector.inputValue() === '', 'Default GL must request account context');
    assert(await page.locator('main table').count() === 0, 'Default GL must not fabricate transactions');
    const count = await selector.locator('option').count() - 1;
    evidence.push({stage:'ledger-default',accountCount:count});
    assert(count >= 33, 'Full account catalog not loaded');
    await checkPage('ledger-default');
    await selector.selectOption('01a0828d-734b-7f91-a220-8757c0148304');
    await page.locator('main h3').filter({hasText:'COD Clearing'}).waitFor();
    assert(await page.locator('main table').count() === 0, 'No COD collection journal exists in this tenant');
    await checkPage('cod-ledger-empty');

    await selector.selectOption('01a0828d-734b-7385-9659-ef041fa3e836');
    await page.locator('main h3').filter({hasText:'Cashbox Main'}).waitFor();
    assert(await page.locator('main table').count() === 0, 'October has no cashbox entries');
    const dates = page.locator('main input[type="date"]');
    await dates.nth(0).fill('2026-09-01');
    await page.locator('main table').getByText('JE-2026-000010', {exact:true}).waitFor();
    const journalRows = await page.locator('main table tbody tr').allTextContents();
    assert(journalRows.length === 5, 'Expected opening row plus four immutable movements');
    assert(journalRows.some(row=>row.includes('JE-2026-000001')) && journalRows.some(row=>row.includes('JE-2026-000002')), 'Missing September history');
    const persistedUrl = page.url();
    await page.reload();
    await page.locator('main table').getByText('JE-2026-000010', {exact:true}).waitFor();
    assert(page.url() === persistedUrl, 'URL filters not persisted');
    assert(await selector.inputValue() === '01a0828d-734b-7385-9659-ef041fa3e836', 'Account not persisted');
    assert(await dates.nth(0).inputValue() === '2026-09-01', 'Date not persisted');
    assert(await page.locator('main table tbody tr').count() === 5, 'Journal history not persisted');
    await checkPage('ledger-history-reloaded');
    await page.screenshot({path:`.artifacts/cod-ledger-audit/${tag}-ledger.png`, fullPage:true});
    return {tag, viewport:page.viewportSize(),journalRows,evidence,passed:true};
}

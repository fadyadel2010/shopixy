async page => {
    const root = '.ai/evidence/smart-offers-20261020';
    const results = [];
    const check = (condition, message) => { if (!condition) throw new Error(message); results.push(message); };
    const dialog = page.getByRole('dialog');
    const field = name => dialog.getByLabel(name, { exact: true });
    const sample = () => dialog.locator('input, select, textarea').evaluateAll(nodes => nodes.map(node => {
        const css = getComputedStyle(node);
        return { name: node.name, type: node.type, value: node.value, checked: node.checked,
            color: css.color, background: css.backgroundColor, opacity: css.opacity, visibility: css.visibility, disabled: node.disabled };
    }));
    await page.setViewportSize({ width: 1440, height: 1000 });
    if (!await dialog.count()) await page.getByRole('button', { name: 'Create offer', exact: true }).click();
    const defaults = await sample();
    await field('Name').fill('UI audit — اختبار');
    await field('Priority').fill('12');
    await field('Delay seconds').fill('10');
    await field('Scroll percent').fill('45');
    await field('Max displays per user').fill('3');
    await field('Title').fill('عرض تجريبي — Preview');
    await field('Button text').fill('Continue — متابعة');
    await field('Coupon code').fill(' audit10 ');
    await field('Discount value').fill('10');
    await field('Priority').evaluate(node => { window.__smartPriority = node; });
    for (const trigger of ['TimeDelay', 'ScrollDepth', 'AddToCart', 'BeforeCheckout', 'ExitIntent']) {
        await field('Trigger type').selectOption(trigger);
    }
    for (const scope of ['Home', 'Product', 'Category', 'Cart', 'Checkout', 'All']) await field('Pages scope').selectOption(scope);
    for (const type of ['FixedAmount', 'Percentage']) await field('Coupon discount type').selectOption(type);
    await field('Status').selectOption('Active');
    await field('Status').selectOption('Inactive');
    await field('Show once per session').uncheck();
    check(await field('Priority').evaluate(node => node === window.__smartPriority), 'Control DOM identity survives quick option changes');
    check(await field('Priority').inputValue() === '12', 'Numeric draft survives option changes');
    check(await dialog.getByRole('heading', { name: 'عرض تجريبي — Preview' }).count() === 1, 'Live preview reflects mixed Arabic/English title');
    check(await dialog.getByText('audit10', { exact: true }).count() === 0, 'Private coupon code is not displayed in preview');
    await field('Priority').fill('');
    check(await field('Priority').inputValue() === '', 'Numeric clear stays empty until submission');
    await field('Priority').fill('12');
    await dialog.getByRole('button', { name: 'Hide preview' }).click();
    await dialog.getByRole('button', { name: 'Preview', exact: true }).click();
    check(await field('Priority').inputValue() === '12', 'Preview visibility does not reset fields');
    await field('Coupon code').fill('   ');
    check(await field('Discount value').isDisabled(), 'Whitespace coupon code disables linked coupon controls');
    await field('Coupon code').fill(' audit10 ');
    check(await field('Discount value').inputValue() === '10', 'Restoring code retains draft coupon amount');
    await field('Trigger type').selectOption('ScrollDepth');
    await field('Scroll percent').fill('101');
    check(!await field('Scroll percent').evaluate(node => node.checkValidity()), 'Out-of-range scroll depth is invalid');
    await field('Scroll percent').fill('45');
    check(await field('Scroll percent').evaluate(node => node.checkValidity()), 'Corrected scroll depth is valid');
    const edited = await sample();
    await field('Name').scrollIntoViewIfNeeded();
    await page.screenshot({ path: `${root}/after-edited-preview.png` });
    const layouts = [];
    for (const size of [{ width: 1280, height: 800 }, { width: 768, height: 900 }, { width: 390, height: 844 }]) {
        await page.setViewportSize(size);
        await field('Name').scrollIntoViewIfNeeded();
        const layout = await dialog.evaluate(node => {
            const rect = node.getBoundingClientRect();
            const footer = node.querySelector('footer').getBoundingClientRect();
            return { dialogWidth: rect.width, dialogLeft: rect.left, dialogRight: rect.right, scrollWidth: node.scrollWidth,
                clientWidth: node.clientWidth, footerTop: footer.top, footerBottom: footer.bottom, viewportHeight: innerHeight };
        });
        check(layout.dialogLeft >= 0 && layout.dialogRight <= size.width && layout.scrollWidth === layout.clientWidth, `No dialog horizontal overflow at ${size.width}`);
        check(layout.footerTop >= 0 && layout.footerBottom <= size.height, `Actions visible at ${size.width}`);
        await field('Image URL').scrollIntoViewIfNeeded();
        await page.screenshot({ path: `${root}/after-${size.width}-scrolled.png` });
        layouts.push({ ...size, ...layout });
    }
    await dialog.getByRole('button', { name: 'Close editor' }).click();
    await page.setViewportSize({ width: 1440, height: 1000 });
    for (let i = 0; i < 3; i++) {
        await page.getByRole('button', { name: 'Create offer', exact: true }).click();
        check(await field('Name').inputValue() === '' && await field('Priority').inputValue() === '0', `Fresh creation defaults on opening ${i + 1}`);
        await field('Name').fill('Discarded draft');
        await page.keyboard.press('Escape');
        check(await dialog.count() === 0, `Escape closes opening ${i + 1}`);
    }
    await page.screenshot({ path: `${root}/after-page.png` });
    await page.getByRole('button', { name: 'AR', exact: true }).click();
    await page.getByRole('button', { name: 'إنشاء عرض', exact: true }).click();
    check(await dialog.getAttribute('dir') === 'rtl', 'Arabic editor uses RTL independently of its portal');
    await dialog.getByLabel('الاسم', { exact: true }).fill('اختبار عربي — English 123');
    await dialog.getByLabel('الأولوية', { exact: true }).fill('12');
    await dialog.getByLabel('العنوان', { exact: true }).fill('عرض تجريبي — Preview');
    await page.screenshot({ path: `${root}/after-rtl-desktop.png` });
    await page.setViewportSize({ width: 390, height: 844 });
    const rtlLayout = await dialog.evaluate(node => ({ scrollWidth: node.scrollWidth, clientWidth: node.clientWidth }));
    check(rtlLayout.scrollWidth === rtlLayout.clientWidth, 'No RTL narrow dialog horizontal overflow');
    await page.screenshot({ path: `${root}/after-rtl-390.png` });
    await dialog.getByRole('button', { name: 'إغلاق المحرر' }).click();
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.getByRole('button', { name: 'EN', exact: true }).click();
    return { results, defaults, edited, layouts, rtlLayout };
}

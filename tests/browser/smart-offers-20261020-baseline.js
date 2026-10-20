async page => {
    const root = '.ai/evidence/smart-offers-20261020';
    const sample = () => page.locator('form').first().locator('input, select, textarea').evaluateAll(nodes => nodes.map(node => {
        const css = getComputedStyle(node);
        return { label: node.closest('label')?.innerText, value: node.value, type: node.type, color: css.color,
            background: css.backgroundColor, opacity: css.opacity, visibility: css.visibility, height: node.clientHeight };
    }));
    const initial = await sample();
    await page.getByLabel('Name', { exact: true }).fill('UI audit — اختبار');
    await page.getByLabel('Priority', { exact: true }).fill('12');
    await page.getByLabel('Delay seconds', { exact: true }).fill('10');
    await page.getByLabel('Scroll percent', { exact: true }).fill('45');
    await page.getByLabel('Max displays per user', { exact: true }).fill('3');
    await page.getByLabel('Title', { exact: true }).fill('عرض تجريبي — Preview');
    await page.getByLabel('Button text', { exact: true }).fill('Continue — متابعة');
    await page.getByLabel('Coupon code', { exact: true }).fill(' audit10 ');
    await page.getByLabel('Discount value', { exact: true }).fill('10');
    const edits = await sample();
    const previewButton = page.getByRole('button', { name: 'Preview', exact: true });
    if (await previewButton.count()) await previewButton.click();
    await page.locator('form').first().evaluate(form => form.querySelector('input').scrollIntoView());
    await page.screenshot({ path: `${root}/before-edited-preview.png` });
    const preview = await sample();
    const priority = page.getByLabel('Priority', { exact: true });
    await priority.evaluate(node => { window.__smartPriority = node; });
    for (const trigger of ['TimeDelay', 'ScrollDepth', 'AddToCart', 'BeforeCheckout', 'ExitIntent']) {
        await page.getByRole('combobox', { name: 'Trigger type', exact: true }).selectOption(trigger);
    }
    const sameNode = await priority.evaluate(node => window.__smartPriority === node);
    await page.getByRole('button', { name: 'Hide preview', exact: true }).click();
    await priority.fill('');
    const cleared = await priority.inputValue();
    await priority.fill('12');
    const repeated = await sample();
    await page.getByRole('button', { name: 'Close editor', exact: true }).click();
    await page.getByRole('button', { name: 'Create offer', exact: true }).click();
    const reopened = await sample();
    return { initial, edits, preview, sameNode, cleared, repeated, reopened };
}

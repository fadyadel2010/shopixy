async page => {
    await page.reload();
    await page.getByRole('button', { name: 'Preview Saved Draft', exact: true }).click();
    const frame = page.frameLocator('iframe');
    await frame.locator('header').waitFor({ timeout: 25000 });
    await frame.locator('footer').waitFor();
    await page.screenshot({ path: '.artifacts/gap-preview-smoke.png' });
    return { header: await frame.locator('header').innerText(), footer: await frame.locator('footer').innerText() };
}

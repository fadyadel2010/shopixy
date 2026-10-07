async page => {
    const session = await page.evaluate(() => {
        sessionStorage.setItem('gap-preview-index', '0');
        sessionStorage.removeItem('gap-preview-results');
        sessionStorage.removeItem('gap-preview-error');
        return { reset: true };
    });
    return session;
}

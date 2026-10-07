async page => await page.evaluate(() => ({
    index: Number(sessionStorage.getItem('gap-preview-index') || '0'),
    error: sessionStorage.getItem('gap-preview-error'),
    result: JSON.parse(sessionStorage.getItem('gap-preview-results') || '[]').at(-1),
}))

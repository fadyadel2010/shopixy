async page => await page.evaluate(() => ({
    themes: JSON.parse(sessionStorage.getItem('gap-preview-results') || '[]'),
    signedMobile: JSON.parse(sessionStorage.getItem('gap-signed-responsive-results') || '[]'),
    convergence: JSON.parse(sessionStorage.getItem('gap-convergence-evidence') || '{}'),
    security: JSON.parse(sessionStorage.getItem('gap-security-evidence') || '{}'),
    middle: JSON.parse(sessionStorage.getItem('gap-middle-results') || '[]'),
}))

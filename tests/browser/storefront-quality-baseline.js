// Read-only against the hosted API. All theme/auth changes below are browser-memory fixtures.
async page => {
    const context = await page.context().browser().newContext({ viewport: { width: 1280, height: 900 } });
    const tab = await context.newPage();
    const results = [];
    try {
        await tab.goto('http://ahramstore.localhost:5174/');
        await tab.locator('header').waitFor();
        const themes = ['modern', 'minimal', 'fashion', 'pearl', 'phoenix', 'hype', 'alizon', 'sharp', 'cosmetics', 'seven'];
        for (const theme of themes) {
            await tab.evaluate(async themeId => {
                const { useStoreConfigStore } = await import('/src/store/storeConfigStore.ts');
                const { useAuthStore } = await import('/src/auth/store/authStore.ts');
                useAuthStore.setState({ status: 'unauthenticated', customer: null, isLoading: false });
                useStoreConfigStore.setState(s => ({ config: { ...s.config, storefrontTheme: themeId } }));
            }, theme);
            await tab.waitForFunction(id => document.querySelector(`[data-product-card-theme="${id}"]`), theme);
            for (const width of [1280, 820, 390]) {
                await tab.setViewportSize({ width, height: 900 });
                const header = tab.locator('header').first();
                const login = header.locator('a[href^="/login"]');
                const menu = header.locator('button[aria-expanded]').first();
                const visibleLogin = await login.first().isVisible().catch(() => false);
                results.push({ theme, width, login: visibleLogin, menu: await menu.isVisible().catch(() => false),
                    loginColor: visibleLogin ? await login.first().evaluate(e => getComputedStyle(e).color) : null,
                    accountAriaLabel: await header.evaluate(e => e.querySelector('a[href="/account"]')?.getAttribute('aria-label') ?? null) });
            }
        }
        return results;
    } finally { await context.close(); }
}

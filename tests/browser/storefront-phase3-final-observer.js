// Fresh app bootstrap, real auth/cart client, no authoring or basket mutations.
async page => {
    const storefront = await page.context().newPage();
    try {
        await storefront.goto('http://ahramstore.localhost:5174/cart');
        await storefront.getByRole('button', { name: 'Logout', exact: true }).waitFor();
        const cart = await storefront.evaluate(async () => {
            const moduleUrl = performance.getEntriesByType('resource').find(e => /\/services\/api\/httpClient\.ts(?:\?|$)/.test(e.name)).name;
            const { httpClient } = await import(moduleUrl);
            const response = await httpClient.get('/api/storefront/cart');
            return { status: response.status, ownerType: response.data.ownerType, items: response.data.items.length, subtotal: response.data.subtotal, discount: response.data.totalDiscount };
        });
        return { observedUtc: new Date().toISOString(), evidence: 'fresh-Storefront-bootstrap-real-runtime-client', cart,
            passed: cart.status === 200 && cart.ownerType === 'customer' && cart.items === 0 && cart.subtotal === 0 && cart.discount === 0 };
    } finally { await storefront.close(); }
}

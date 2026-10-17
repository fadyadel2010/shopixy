async page => {
    await page.goto('http://ahramstore.localhost:5174/cart');
    await page.getByRole('link', { name: 'Account', exact: true }).waitFor();
    await page.getByRole('heading', { name: 'Your cart is empty', exact: true }).waitFor();
    const observed = await page.evaluate(async () => {
        const response = await fetch('/api/storefront/cart', { credentials: 'include' });
        const data = await response.json();
        return { http: response.status, itemCount: data.itemCount, hasItems: Array.isArray(data.items), empty: data.items?.length === 0 };
    });
    // Assert DOM plus the actual hosted contract, not a newly imported Vite module
    // that can create a second store identity with different HMR query versions.
    return {
        observedUtc: new Date().toISOString(), layer: 'hosted-authenticated-cart-read',
        authenticatedAccountLink: true, emptyCartHeading: true,
        http: observed.http, backendItemCount: observed.itemCount,
        backendHasItems: observed.hasItems,
        passed: observed.http === 200 && observed.itemCount === 0 && observed.empty,
        checkoutButtons: await page.getByRole('button', { name: /checkout/i }).count(),
        explicitOrderOrCartMutations: 0, loginMayHaveMergedSessionCart: true,
    };
}

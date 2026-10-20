async page => {
    const marker = 'CCQA20261019-COD-BEFORE';
    const cart = await page.evaluate(async () => (await (await import('/src/services/api/httpClient.ts')).httpClient.get('/api/storefront/cart')).data);
    if (cart.items.length) throw Error('Existing cart is not empty; inspect before mutation');
    await page.getByRole('region', { name: 'Superstore buying confidence' }).getByRole('button', { name: 'Add to Cart', exact: true }).click();
    await page.goto('http://ahramstore.localhost:5174/cart');
    return {marker,text:await page.locator('main').innerText()};
}

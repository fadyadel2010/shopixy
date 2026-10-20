async page => {
    const storefront = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    const read = () => storefront.evaluate(async () => {
        const loaded = performance.getEntriesByType('resource').find(entry => entry.name.includes('/src/store/checkoutStore.ts'))?.name;
        if (!loaded) throw new Error('Checkout runtime module not loaded');
        const s = (await import(loaded)).useCheckoutStore.getState();
        return {paymentMethod: s.paymentMethod, paymentMethods: s.paymentMethods.map(m => m.code), pricing: s.pricing, checkoutSessionId: s.checkoutSessionId, proofFileKey: s.manualWalletProofFileKey, step: s.step};
    });
    const before = await read();
    await storefront.reload();
    await storefront.getByRole('heading', {name: 'Checkout', exact: true}).waitFor();
    await storefront.getByRole('button', {name: 'Back to Payment', exact: true}).click();
    await storefront.locator('input[name=paymentMethod][value=MANUALWALLET]').waitFor({state: 'attached'});
    const after = await read();
    await storefront.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/wallet-after-checkout-refresh.png', fullPage: true});
    return {before, after, visible: await storefront.getByText('P5QA20261017-817 TEST ONLY Wallet', {exact: true}).isVisible()};
}

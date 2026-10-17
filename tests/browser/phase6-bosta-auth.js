async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    if (!admin) throw new Error('Existing Admin tab required');
    return await admin.evaluate(async () => {
        const accountId = '14030fd4-b1b5-4fed-bea5-80fb735c5f30';
        const { runCarrierConnectionTest } = await import('/src/modules/shipping-administration/shippingAdministrationApi.js');
        const result = await runCarrierConnectionTest(accountId, { useSavedCredentials: true, saveOnly: false, useSandbox: false });
        return { observedUtc: new Date().toISOString(), accountId,
            succeeded: result.succeeded, failureCode: result.failureCode,
            providerOperation: 'GET /pickup-locations', shipmentsCreated: 0,
            localMutation: 'connection-test and audit records only; no credentials or configuration replaced' };
    });
}

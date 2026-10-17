async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    if (!admin) throw new Error('Existing authenticated Admin tab required');
    return await admin.evaluate(async () => {
        const id = '14030fd4-b1b5-4fed-bea5-80fb735c5f30';
        const response = await fetch(`/api/admin/shipping/carrier-accounts/${id}/bosta/pickup-locations`, { credentials: 'include', cache: 'no-store' });
        const result = await response.json();
        return { observedUtc: new Date().toISOString(), http: response.status, accountId: result.accountId,
            environment: result.environment, errorCode: result.code, noStore: response.headers.get('cache-control'),
            locations: result.locations?.map(location => ({ id: location.id, isDefault: location.isDefault, countryCode: location.countryCode,
                cityId: location.cityId, districtId: location.districtId })),
            rawStreetOrContactExposed: result.locations?.some(location => ['firstLine', 'phone', 'contactPerson', 'street'].some(key => key in location)),
            localMutations: 0, liveShipmentsCreated: 0, pickupRequestsCreated: 0, cancellationsSent: 0 };
    });
}

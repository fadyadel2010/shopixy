async page => {
    const accountId = '14030fd4-b1b5-4fed-bea5-80fb735c5f30'
    const read = () => page.evaluate(async id => {
        const response = await fetch('/api/admin/shipping/carrier-accounts', { credentials: 'include', cache: 'no-store' })
        if (!response.ok) throw new Error('Account read failed: ' + response.status)
        const account = (await response.json()).find(item => item.id === id)
        if (!account) throw new Error('Authorized saved Bosta account is unavailable.')
        return {
            configuration: { id: account.id, state: account.state, environment: account.environment,
                activeCredentialReferenceId: account.activeCredentialReferenceId, warehouseAssignments: account.warehouseAssignments,
                policies: account.policies, serviceLevels: account.serviceLevels, savedCredentialFields: account.savedCredentialFields },
            enabled: account.integrationReadiness?.enabled, canExecute: account.integrationReadiness?.canExecute,
            blockers: account.integrationReadiness?.blockers,
        }
    }, accountId)
    const before = await read()
    const writes = []
    const onRequest = request => {
        if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method()) && /\/api\/(admin\/shipping|shippingmethods)/.test(request.url()))
            writes.push({ method: request.method(), category: 'shipping-write' })
    }
    page.on('request', onRequest)
    try {
        await page.getByRole('tab', { name: 'Carrier Accounts', exact: true }).click()
        const [response] = await Promise.all([
            page.waitForResponse(response => response.url().endsWith('/bosta/pickup-locations') && response.request().method() === 'GET', { timeout: 25000 }),
            page.getByRole('button', { name: 'Load saved-account pickup locations', exact: true }).click(),
        ])
        const data = await response.json()
        const verifiedMessage = page.getByText(/^Provider default verified:/)
        await verifiedMessage.waitFor()
        const proofVisible = await verifiedMessage.isVisible()
        const after = await read()
        const [refresh] = await Promise.all([
            page.waitForResponse(response => response.url().endsWith('/admin/shipping/carrier-accounts') && response.request().method() === 'GET', { timeout: 15000 }),
            page.getByRole('button', { name: 'Refresh readiness', exact: true }).click(),
        ])
        if (!refresh.ok()) throw new Error('Readiness refresh failed: ' + refresh.status())
        const booking = page.getByRole('region', { name: 'Activation and booking eligibility' }).filter({ hasText: 'CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED' })
        await booking.first().waitFor()
        return { observedUtc: new Date().toISOString(), loadedApi: true, discoveryStatus: response.status(),
            noStore: response.headers()['cache-control'], accountId: data.accountId, environment: data.environment,
            pickupSelection: data.pickupSelection,
            locations: data.locations.map(location => ({ id: location.id, isDefault: location.isDefault, countryCode: location.countryCode,
                countryId: location.countryId, cityId: location.cityId, districtId: location.districtId })),
            providerDefaultProofVisible: proofVisible, automaticFieldSelection: await page.getByRole('combobox', { name: 'Choose verified pickup location', exact: true }).inputValue(),
            before, after, accountConfigurationUnchanged: JSON.stringify(before.configuration) === JSON.stringify(after.configuration),
            readinessRefreshStatus: refresh.status(), visibleBookingBoundary: await booking.first().isVisible(), writes,
            canonicalPublication: false, carrierActivation: false, liveShipmentEffects: 0,
        }
    } finally { page.removeListener('request', onRequest) }
}

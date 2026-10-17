async page => {
    const reference = page.context().pages().find(item => item.url().startsWith('https://app.easy-orders.net/'))
    if (!reference) throw new Error('Existing authenticated reference session is unavailable.')
    const metadata = await reference.evaluate(() => ({
        observedUtc: new Date().toISOString(), origin: location.origin, page: location.hash,
        authenticatedShippingFormPresent: !!document.querySelector('input[name=enableShippingIntegration]'),
        labels: [...document.querySelectorAll('label')].map(element => element.textContent),
        fieldNames: [...document.querySelectorAll('input[name]')].map(element => element.name),
        tableColumns: [...document.querySelectorAll('th')].map(element => element.textContent),
        cityRows: document.querySelectorAll('tbody tr').length,
        saveButtonPresent: [...document.querySelectorAll('button')].some(element => element.innerText === 'حفظ الاعدادات'),
    }))
    return { ...metadata,
        network: (reference.__shippingReadOnlyEvidence || []).filter(entry => /shipping-method|tenant-shipping-settings|tenant-cities-settings|countries$/.test(entry.path)),
        referenceAccountMutationsSubmitted: 0,
        boundary: 'No input values, request/response bodies, authorization headers, cookies, session storage, API keys or personal data retained. Non-GET/HEAD/OPTIONS requests blocked during inspection. Save and activation were not clicked. Existing saved settings were reloaded, not edited.',
        dialogs: {
            Bosta: { fields: ['isActive', 'metadata.apiKey', 'metadata.canOpenPackage'], secretValueRead: false },
            Mylerz: { fields: ['isActive', 'metadata.username', 'metadata.password', 'metadata.serviceType', 'metadata.country', 'metadata.wharehouseName'], secretValueRead: false },
        },
        unknowns: ['Save request method/body and server validation were deliberately not exercised.', 'No carrier is activated; configured-only fields or provider-side eligibility cannot be established.', 'Reference UI does not establish Bosta API or canonical geography authority.'],
    }
}

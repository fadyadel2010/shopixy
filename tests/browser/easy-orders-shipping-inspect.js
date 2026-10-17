async page => {
    const reference = page.context().pages().find(item => item.url().startsWith('https://app.easy-orders.net/'))
    if (!reference) return { available: false }
    // Read DOM structure only. Never return input values, storage, headers or bodies.
    return await reference.evaluate(() => {
        const targets = [...document.querySelectorAll('img')].filter(element => /bosta|mylerz/i.test((element.alt || '') + element.src))
        return {
            carrierImages: targets.map(element => ({ alt: element.alt, parentText: element.parentElement?.textContent,
                grandparentText: element.parentElement?.parentElement?.textContent,
                parentTag: element.parentElement?.tagName,
                controls: [...(element.parentElement?.parentElement?.querySelectorAll('button,input') || [])].map(control => ({
                    tag: control.tagName, role: control.getAttribute('role'), type: control.getAttribute('type'), label: control.getAttribute('aria-label'), text: control.textContent,
                })) })),
            shippingFields: [...document.querySelectorAll('input[name]')].filter(element => ['enableShippingIntegration', 'countryId', 'shippingCostType', 'shippingCost'].includes(element.name))
                .map(element => ({ name: element.name, type: element.type, checked: ['checkbox', 'radio'].includes(element.type) ? element.checked : undefined,
                    required: element.required, disabled: element.disabled })),
            headings: [...document.querySelectorAll('h1,h2,h3,h4,h5')].map(element => element.textContent),
        }
    })
}

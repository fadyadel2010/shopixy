async page => {
    // Customer already authenticated through the normal login form. Follow only
    // existing order links; never create an order or submit a fulfillment/payment command.
    const links = await page.locator('main a[href$="/track"]').evaluateAll(nodes => nodes.map(n => n.getAttribute('href')))
    if (!links.length) return { authenticatedAccountPage: true, existingTrackingOrders: 0, disposition: 'NOT VERIFIED' }
    const results = []
    const isArabic = await page.evaluate(() => document.documentElement.dir === 'rtl' || document.documentElement.lang.startsWith('ar'))
    for (const href of links.slice(0, 3)) {
        const responsePromise = page.waitForResponse(r => /\/api\/storefront\/orders\/[0-9a-f-]+\/track(?:\?|$)/i.test(r.url()), { timeout: 30000 })
        await page.goto(`http://ahramstore.localhost:5174${href}`)
        const response = await responsePromise
        if (response.status() !== 200) throw new Error(`Existing customer tracking HTTP ${response.status()}`)
        const raw = await response.json()
        const dto = raw.data ?? raw
        await page.locator('#tracking-heading').waitFor()
        const heading = await page.locator('#tracking-heading').innerText()
        if (dto.status === 'Completed' && heading !== (isArabic ? 'تم التسليم' : 'Delivered'))
            throw new Error('COMPLETED_TRACKING_PRESENTATION_MISMATCH: expected Delivered, received ' + heading)
        results.push({ orderStatus: dto.orderStatus ?? dto.status, shippingStatus: dto.shippingStatus,
            paymentStatus: dto.paymentStatus, paymentMethodCode: dto.paymentMethodCode,
            total: dto.total, shippingCost: dto.shippingCost, status: response.status(), heading })
    }
    return { observedUtc: new Date().toISOString(), unmocked: true, language: isArabic ? 'ar' : 'en', existingTrackingOrders: results.length,
        newOrders: 0, fulfillmentCommands: 0, financialCommands: 0, results }
}

// Presentation admission only: this never grants lifecycle or provider authority.
export function storedLabelLink(shipment) {
    const formats = { pdf: 'application/pdf', png: 'image/png', zpl: 'text/plain' }
    const format = shipment.labelFormat?.toLowerCase()
    if (formats[format] && typeof shipment.labelData === 'string' && shipment.labelData.length > 0 &&
        shipment.labelData.length <= 12 * 1024 * 1024 && /^[A-Za-z0-9+/]+={0,2}$/.test(shipment.labelData)) {
        return { href: `data:${formats[format]};base64,${shipment.labelData}`, download: `shipping-label.${format}` }
    }
    try {
        const url = new URL(shipment.labelUrl)
        if (url.protocol === 'https:' || url.protocol === 'http:') return { href: url.href }
    } catch { /* No supported stored label. Do not invent a retrieval operation. */ }
    return null
}

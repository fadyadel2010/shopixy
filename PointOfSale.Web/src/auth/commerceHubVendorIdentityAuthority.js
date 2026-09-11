// .NET Guid values in this contract include RFC 9562 UUIDv7 identifiers.
// Keep the RFC variant check while accepting all currently assigned UUID versions.
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

export const AFFILIATE_ATTRIBUTION_WARNING_CODE = 'AFFILIATE_ATTRIBUTION_UNAVAILABLE'

const normalizeString = (value) => (typeof value === 'string' ? value.trim() : '')

const ORDER_SOURCES = new Set(['Ecommerce', 'POS', 'Amazon', 'Marketplace', 'Manual'])
const AFFILIATE_ATTRIBUTION_SOURCES = new Set(['ReferralLink', 'CouponCode', 'ManualAdmin'])

const normalizeOrderSource = (value) => normalizeString(value ?? '')
const normalizeGuid = (value) => normalizeString(value)

const isFiniteMoney = (value) => Number.isFinite(Number(value))

const normalizeOptionalJson = (value, fieldName) => {
    if (value == null) {
        return null
    }

    if (typeof value !== 'string') {
        throw new Error(`Marketplace order lineage authority rejected malformed ${fieldName} metadata.`)
    }

    const trimmed = value.trim()
    if (!trimmed) {
        return null
    }

    try {
        JSON.parse(trimmed)
    } catch {
        throw new Error(`Marketplace order lineage authority rejected malformed ${fieldName} metadata.`)
    }

    return trimmed
}

export const resolveCurrentVendorIdentityAuthority = (user) => {
    const tenantId = normalizeString(user?.tenantId)
    const userId = normalizeString(user?.id ?? user?.userId)

    if (!GUID_PATTERN.test(tenantId) || !GUID_PATTERN.test(userId)) {
        return null
    }

    return { tenantId, userId }
}

export const isCurrentVendorTenantAuthority = (value, currentUser) => {
    const current = resolveCurrentVendorIdentityAuthority(currentUser)
    if (!current || !value || typeof value !== 'object' || Array.isArray(value)) {
        return false
    }

    const dtoTenantId = normalizeString(value.tenantId ?? value.TenantId ?? value.vendorId ?? value.VendorId)
    return GUID_PATTERN.test(dtoTenantId) && dtoTenantId.toLowerCase() === current.tenantId.toLowerCase()
}

export const deriveCurrentVendorScopedDto = (value, currentUser) => {
    if (!value || typeof value !== 'object' || Array.isArray(value)) {
        return null
    }

    const current = resolveCurrentVendorIdentityAuthority(currentUser)
    if (!current) {
        return null
    }

    const dtoTenantId = normalizeString(value.tenantId ?? value.TenantId ?? value.vendorId ?? value.VendorId)
    if (!GUID_PATTERN.test(dtoTenantId) || !isCurrentVendorTenantAuthority(value, currentUser)) {
        return null
    }

    return value
}

export const deriveCurrentVendorScopedArray = (values, currentUser) => {
    if (!Array.isArray(values)) {
        return []
    }

    const accepted = []
    for (const value of values) {
        const acceptedValue = deriveCurrentVendorScopedDto(value, currentUser)
        if (!acceptedValue) {
            return []
        }
        accepted.push(acceptedValue)
    }

    return accepted
}

export const requireCurrentVendorOrderVisibilityAuthority = (value, currentUser, expectedOrderId) => {
    const scopedValue = deriveCurrentVendorScopedDto(value, currentUser)
    const routeOrderId = normalizeString(expectedOrderId)
    const dtoOrderId = normalizeString(scopedValue?.orderId ?? scopedValue?.OrderId)

    if (!scopedValue || !routeOrderId || !dtoOrderId || dtoOrderId !== routeOrderId) {
        throw new Error('CommerceHub vendor order visibility authority rejected detail evidence for the current route.')
    }

    requireBackendOrderSourceAuthority(scopedValue)

    return admitBackendAffiliateAttributionPresentation(scopedValue)
}

export const requireCurrentVendorOrderVisibilityListAuthority = (response, currentUser) => {
    if (!response || typeof response !== 'object' || Array.isArray(response) || !Array.isArray(response.items)) {
        throw new Error('CommerceHub vendor order visibility authority rejected malformed list evidence.')
    }

    const seenOrderIds = new Set()
    const acceptedOrders = []
    for (const order of response.items) {
        const scopedOrder = deriveCurrentVendorScopedDto(order, currentUser)
        const orderId = normalizeString(scopedOrder?.orderId ?? scopedOrder?.OrderId)

        if (!scopedOrder || !orderId || seenOrderIds.has(orderId.toLowerCase())) {
            throw new Error('CommerceHub vendor order visibility authority rejected foreign, malformed, or duplicate list evidence.')
        }

        requireBackendOrderSourceAuthority(scopedOrder)
        acceptedOrders.push(admitBackendAffiliateAttributionPresentation(scopedOrder))
        seenOrderIds.add(orderId.toLowerCase())
    }

    return {
        ...response,
        items: acceptedOrders,
    }
}

export const requireBackendOrderSourceAuthority = (order) => {
    const source = normalizeOrderSource(order?.source ?? order?.Source)
    const sourceLabel = normalizeString(order?.sourceLabel ?? order?.SourceLabel)
    const isMarketplaceOrder = order?.isMarketplaceOrder === true || order?.IsMarketplaceOrder === true

    if (!ORDER_SOURCES.has(source)) {
        throw new Error('CommerceHub vendor order visibility authority rejected missing or malformed order source evidence.')
    }

    if (isMarketplaceOrder && source !== 'Marketplace') {
        throw new Error('CommerceHub vendor order visibility authority rejected contradictory marketplace source evidence.')
    }

    if (!isMarketplaceOrder && source !== 'Ecommerce') {
        throw new Error('CommerceHub vendor order visibility authority rejected non-storefront source evidence.')
    }

    if (sourceLabel && sourceLabel !== source) {
        throw new Error('CommerceHub vendor order visibility authority rejected contradictory source label evidence.')
    }

    if (isMarketplaceOrder) {
        if (order?.affiliateAttribution != null || order?.AffiliateAttribution != null) {
            throw new Error('CommerceHub vendor order visibility authority rejected affiliate evidence on a Marketplace order.')
        }
        requireBackendMarketplaceLineageAuthority(order)
    } else if (order?.marketplaceLineage != null || order?.MarketplaceLineage != null) {
        throw new Error('Marketplace order lineage authority rejected marketplace evidence on a non-marketplace order.')
    }

    return {
        source,
        label: source === 'Ecommerce' ? 'Storefront' : 'Marketplace',
        tone: source === 'Ecommerce' ? 'slate' : 'sky',
    }
}

export const requireBackendMarketplaceLineageAuthority = (order) => {
    const lineage = order?.marketplaceLineage ?? order?.MarketplaceLineage
    if (!lineage || typeof lineage !== 'object' || Array.isArray(lineage)) {
        throw new Error('Marketplace order lineage authority rejected missing lineage evidence.')
    }

    const orderTenantId = normalizeGuid(order?.tenantId ?? order?.TenantId)
    const orderId = normalizeGuid(order?.orderId ?? order?.OrderId)
    const sourceOrderId = normalizeGuid(order?.sourceOrderId ?? order?.SourceOrderId)
    const tenantId = normalizeGuid(lineage.tenantId ?? lineage.TenantId)
    const lineageOrderId = normalizeGuid(lineage.orderId ?? lineage.OrderId)
    const marketplaceOrderId = normalizeGuid(lineage.marketplaceOrderId ?? lineage.MarketplaceOrderId)
    const vendorId = normalizeGuid(lineage.vendorId ?? lineage.VendorId)
    const externalCustomerId = normalizeGuid(lineage.externalCustomerId ?? lineage.ExternalCustomerId)
    const shippingMethodId = normalizeGuid(lineage.shippingMethodId ?? lineage.ShippingMethodId)
    const marketplaceName = normalizeString(lineage.marketplaceName ?? lineage.MarketplaceName)
    const marketplaceOrderNumber = normalizeString(lineage.marketplaceOrderNumber ?? lineage.MarketplaceOrderNumber)
    const marketplaceSubOrderNumber = normalizeString(lineage.marketplaceSubOrderNumber ?? lineage.MarketplaceSubOrderNumber)
    const orderNumber = normalizeString(order?.orderNumber ?? order?.OrderNumber)
    const vendorName = normalizeString(lineage.vendorName ?? lineage.VendorName)
    const sourceType = normalizeString(lineage.marketplaceSourceType ?? lineage.MarketplaceSourceType)
    const marketplaceStatus = normalizeString(lineage.marketplaceStatus ?? lineage.MarketplaceStatus)
    const marketplacePaymentStatus = normalizeString(lineage.marketplacePaymentStatus ?? lineage.MarketplacePaymentStatus)
    const currency = normalizeString(lineage.currency ?? lineage.Currency)
    const paymentReferenceId = normalizeString(lineage.paymentReferenceId ?? lineage.PaymentReferenceId)
    const shippingMethodName = normalizeString(lineage.shippingMethodName ?? lineage.ShippingMethodName)
    const subtotal = lineage.subtotal ?? lineage.Subtotal
    const shipping = lineage.shipping ?? lineage.Shipping
    const total = lineage.total ?? lineage.Total
    const paymentSnapshotJson = normalizeOptionalJson(lineage.paymentSnapshotJson ?? lineage.PaymentSnapshotJson, 'payment')
    const shippingSnapshotJson = normalizeOptionalJson(lineage.shippingSnapshotJson ?? lineage.ShippingSnapshotJson, 'fulfillment')
    const commissionSnapshotJson = normalizeOptionalJson(lineage.commissionSnapshotJson ?? lineage.CommissionSnapshotJson, 'financial')

    if (!GUID_PATTERN.test(orderTenantId) ||
        !GUID_PATTERN.test(orderId) ||
        !GUID_PATTERN.test(tenantId) ||
        !GUID_PATTERN.test(lineageOrderId) ||
        !GUID_PATTERN.test(marketplaceOrderId) ||
        !GUID_PATTERN.test(vendorId) ||
        tenantId.toLowerCase() !== orderTenantId.toLowerCase() ||
        lineageOrderId.toLowerCase() !== orderId.toLowerCase() ||
        vendorId.toLowerCase() !== orderTenantId.toLowerCase()) {
        throw new Error('Marketplace order lineage authority rejected cross-scope lineage evidence.')
    }

    if (!GUID_PATTERN.test(sourceOrderId) || sourceOrderId.toLowerCase() !== marketplaceOrderId.toLowerCase()) {
        throw new Error('Marketplace order lineage authority rejected source identity evidence.')
    }

    if ((externalCustomerId && !GUID_PATTERN.test(externalCustomerId)) ||
        (shippingMethodId && !GUID_PATTERN.test(shippingMethodId))) {
        throw new Error('Marketplace order lineage authority rejected malformed external or fulfillment identity evidence.')
    }

    if (marketplaceName !== 'CommerceHub' ||
        sourceType !== 'Marketplace' ||
        !marketplaceOrderNumber ||
        !marketplaceSubOrderNumber ||
        (orderNumber && marketplaceSubOrderNumber !== orderNumber) ||
        !vendorName ||
        !marketplaceStatus ||
        !marketplacePaymentStatus ||
        !currency ||
        !isFiniteMoney(subtotal) ||
        !isFiniteMoney(shipping) ||
        !isFiniteMoney(total)) {
        throw new Error('Marketplace order lineage authority rejected incomplete lineage evidence.')
    }

    return {
        tenantId,
        orderId: lineageOrderId,
        marketplaceOrderId,
        marketplaceName,
        marketplaceOrderNumber,
        marketplaceSubOrderNumber,
        externalCustomerId: externalCustomerId || null,
        vendorId,
        vendorName,
        marketplaceSourceType: sourceType,
        marketplaceStatus,
        marketplacePaymentStatus,
        currency,
        subtotal: Number(subtotal),
        shipping: Number(shipping),
        total: Number(total),
        paymentReferenceId: paymentReferenceId || null,
        paymentSnapshotJson,
        shippingMethodId: shippingMethodId || null,
        shippingMethodName: shippingMethodName || null,
        shippingSnapshotJson,
        commissionSnapshotJson,
    }
}

export const requireBackendAffiliateAttributionAuthority = (order) => {
    const attribution = order?.affiliateAttribution ?? order?.AffiliateAttribution
    if (attribution == null) {
        return null
    }

    if (!attribution || typeof attribution !== 'object' || Array.isArray(attribution)) {
        throw new Error('Affiliate order lineage authority rejected malformed attribution evidence.')
    }

    const orderTenantId = normalizeString(order?.tenantId ?? order?.TenantId)
    const orderId = normalizeString(order?.orderId ?? order?.OrderId)
    const tenantId = normalizeString(attribution.tenantId ?? attribution.TenantId)
    const attributionOrderId = normalizeString(attribution.orderId ?? attribution.OrderId)
    const attributionId = normalizeString(attribution.attributionId ?? attribution.AttributionId)
    const affiliateAccountId = normalizeString(attribution.affiliateAccountId ?? attribution.AffiliateAccountId)
    const referralCode = normalizeString(attribution.referralCode ?? attribution.ReferralCode)
    const promotionLinkId = normalizeString(attribution.promotionLinkId ?? attribution.PromotionLinkId)
    const source = normalizeString(attribution.source ?? attribution.Source)
    const attributedAtUtc = normalizeString(attribution.attributedAtUtc ?? attribution.AttributedAtUtc)
    const attributedAt = Date.parse(attributedAtUtc)

    if (!GUID_PATTERN.test(orderTenantId) ||
        !GUID_PATTERN.test(orderId) ||
        !GUID_PATTERN.test(tenantId) ||
        !GUID_PATTERN.test(attributionOrderId) ||
        tenantId.toLowerCase() !== orderTenantId.toLowerCase() ||
        attributionOrderId.toLowerCase() !== orderId.toLowerCase()) {
        const error = new Error('Affiliate order lineage authority rejected cross-order attribution evidence.')
        error.code = 'AFFILIATE_ATTRIBUTION_AUTHORITY_CONTRADICTION'
        throw error
    }

    if (!GUID_PATTERN.test(attributionId) ||
        !GUID_PATTERN.test(affiliateAccountId) ||
        (promotionLinkId && !GUID_PATTERN.test(promotionLinkId)) ||
        !referralCode ||
        !AFFILIATE_ATTRIBUTION_SOURCES.has(source) ||
        !Number.isFinite(attributedAt)) {
        throw new Error('Affiliate order lineage authority rejected incomplete attribution evidence.')
    }

    return {
        tenantId,
        orderId: attributionOrderId,
        attributionId,
        affiliateAccountId,
        referralCode,
        promotionLinkId: promotionLinkId || null,
        source,
        attributedAtUtc,
    }
}

export const admitBackendAffiliateAttributionPresentation = (order) => {
    const attribution = order?.affiliateAttribution ?? order?.AffiliateAttribution
    if (attribution == null) {
        return order
    }

    try {
        const admittedAttribution = requireBackendAffiliateAttributionAuthority(order)
        return {
            ...order,
            affiliateAttribution: admittedAttribution,
        }
    } catch (error) {
        if (error?.code === 'AFFILIATE_ATTRIBUTION_AUTHORITY_CONTRADICTION') {
            throw error
        }

        return {
            ...order,
            affiliateAttribution: null,
            AffiliateAttribution: null,
            affiliateAttributionWarning: {
                code: AFFILIATE_ATTRIBUTION_WARNING_CODE,
                message: 'Affiliate attribution is unavailable for this order.',
            },
        }
    }
}

export const deriveCurrentStoreIdentityPresentation = (value, currentUser) => {
    const scopedValue = deriveCurrentVendorScopedDto(value, currentUser)
    if (!scopedValue) {
        return null
    }

    const storeName = normalizeString(scopedValue.storeName ?? scopedValue.StoreName)
    const storefrontStatus = normalizeString(scopedValue.storefrontStatus ?? scopedValue.StorefrontStatus)
        .toLowerCase()
    const themeId = normalizeString(scopedValue.themeId ?? scopedValue.ThemeId)
    const rowVersion = normalizeString(scopedValue.rowVersion ?? scopedValue.RowVersion)

    if (!storeName || !themeId || !['live', 'maintenance', 'disabled'].includes(storefrontStatus) || !rowVersion) {
        return null
    }

    return {
        ...scopedValue,
        storeName,
        storefrontStatus,
        themeId,
        rowVersion,
    }
}

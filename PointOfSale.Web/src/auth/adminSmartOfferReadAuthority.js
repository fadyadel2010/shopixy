const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const STATUSES = new Set(['Active', 'Inactive', 0, 1])
const TRIGGER_TYPES = new Set(['ExitIntent', 'TimeDelay', 'ScrollDepth', 'AddToCart', 'BeforeCheckout', 0, 1, 2, 3, 4])
const PAGE_SCOPES = new Set(['All', 'Home', 'Product', 'Category', 'Cart', 'Checkout', 0, 1, 2, 3, 4, 5])
const DISCOUNT_TYPES = new Set(['Percentage', 'FixedAmount', 0, 1])

const normalizeString = (value) => (typeof value === 'string' ? value.trim() : '')
const normalizeGuid = (value) => normalizeString(value)
const normalizeCurrency = (value) => normalizeString(value).toUpperCase()
const isFiniteNumber = (value) => Number.isFinite(Number(value))
const isOptionalFiniteNumber = (value) => value == null || isFiniteNumber(value)
const isOptionalInteger = (value) => value == null || Number.isInteger(Number(value))
const isOptionalDate = (value) => value == null || value === '' || Number.isFinite(Date.parse(value))

export const requireAdminSmartOfferReadAuthority = (offer, currentUser, expectedOfferId = null) => {
    if (!offer || typeof offer !== 'object' || Array.isArray(offer)) {
        throw new Error('Smart Offer read authority rejected malformed DTO evidence.')
    }

    const userTenantId = normalizeGuid(currentUser?.tenantId)
    const tenantId = normalizeGuid(offer.tenantId ?? offer.TenantId)
    const offerId = normalizeGuid(offer.id ?? offer.Id)
    const expectedId = expectedOfferId == null ? null : normalizeGuid(expectedOfferId)
    const name = normalizeString(offer.name ?? offer.Name)
    const popupTitle = normalizeString(offer.popupTitle ?? offer.PopupTitle)
    const buttonText = normalizeString(offer.buttonText ?? offer.ButtonText)
    const currency = normalizeCurrency(offer.currency ?? offer.Currency)
    const eligibilitySummary = normalizeString(offer.eligibilitySummary ?? offer.EligibilitySummary)
    const rowVersion = normalizeString(offer.rowVersion ?? offer.RowVersion)
    const status = offer.status ?? offer.Status
    const triggerType = offer.triggerType ?? offer.TriggerType
    const pagesScope = offer.pagesScope ?? offer.PagesScope
    const discountType = offer.couponDiscountType ?? offer.CouponDiscountType

    if (!GUID_PATTERN.test(userTenantId) ||
        !GUID_PATTERN.test(tenantId) ||
        tenantId.toLowerCase() !== userTenantId.toLowerCase() ||
        !GUID_PATTERN.test(offerId) ||
        (expectedId && offerId.toLowerCase() !== expectedId.toLowerCase())) {
        throw new Error('Smart Offer read authority rejected foreign or route-mismatched DTO evidence.')
    }

    if (!name ||
        !popupTitle ||
        !buttonText ||
        !currency ||
        !eligibilitySummary ||
        !rowVersion ||
        !Number.isInteger(Number(rowVersion)) ||
        Number(rowVersion) <= 0 ||
        !STATUSES.has(status) ||
        !TRIGGER_TYPES.has(triggerType) ||
        !PAGE_SCOPES.has(pagesScope) ||
        !isOptionalInteger(offer.priority ?? offer.Priority) ||
        !isOptionalInteger(offer.delaySeconds ?? offer.DelaySeconds) ||
        !isOptionalInteger(offer.scrollPercent ?? offer.ScrollPercent) ||
        !isOptionalInteger(offer.maxDisplaysPerUser ?? offer.MaxDisplaysPerUser) ||
        !isOptionalInteger(offer.usageLimit ?? offer.UsageLimit) ||
        !isOptionalInteger(offer.usageCount ?? offer.UsageCount) ||
        !isOptionalInteger(offer.usageLimitPerCustomer ?? offer.UsageLimitPerCustomer) ||
        !isOptionalFiniteNumber(offer.couponDiscountValue ?? offer.CouponDiscountValue) ||
        !isOptionalFiniteNumber(offer.couponMinOrderAmount ?? offer.CouponMinOrderAmount) ||
        !isOptionalFiniteNumber(offer.couponMaxDiscountAmount ?? offer.CouponMaxDiscountAmount) ||
        !isOptionalDate(offer.validFrom ?? offer.ValidFrom) ||
        !isOptionalDate(offer.validTo ?? offer.ValidTo) ||
        !isOptionalDate(offer.createdAt ?? offer.CreatedAt) ||
        !isOptionalDate(offer.updatedAt ?? offer.UpdatedAt)) {
        throw new Error('Smart Offer read authority rejected incomplete DTO evidence.')
    }

    if (discountType != null && !DISCOUNT_TYPES.has(discountType)) {
        throw new Error('Smart Offer read authority rejected unsupported discount metadata evidence.')
    }

    return offer
}

export const requireAdminSmartOfferListReadAuthority = (offers, currentUser) => {
    if (!Array.isArray(offers)) {
        throw new Error('Smart Offer read authority rejected malformed list evidence.')
    }

    const seenOfferIds = new Set()
    return offers.map((offer) => {
        const acceptedOffer = requireAdminSmartOfferReadAuthority(offer, currentUser)
        const offerId = normalizeGuid(acceptedOffer.id ?? acceptedOffer.Id).toLowerCase()

        if (seenOfferIds.has(offerId)) {
            throw new Error('Smart Offer read authority rejected duplicate list evidence.')
        }

        seenOfferIds.add(offerId)
        return acceptedOffer
    })
}

export const requireAdminSmartOfferStatsReadAuthority = (stats, currentUser) => {
    if (!stats || typeof stats !== 'object' || Array.isArray(stats)) {
        throw new Error('Smart Offer read authority rejected malformed stats evidence.')
    }

    const userTenantId = normalizeGuid(currentUser?.tenantId)
    const tenantId = normalizeGuid(stats.tenantId ?? stats.TenantId)
    const currency = normalizeCurrency(stats.currency ?? stats.Currency)

    if (!GUID_PATTERN.test(userTenantId) ||
        !GUID_PATTERN.test(tenantId) ||
        tenantId.toLowerCase() !== userTenantId.toLowerCase() ||
        !currency ||
        !isOptionalInteger(stats.totalOffers ?? stats.TotalOffers) ||
        !isOptionalInteger(stats.activeOffers ?? stats.ActiveOffers) ||
        !isOptionalInteger(stats.exitIntentOffers ?? stats.ExitIntentOffers)) {
        throw new Error('Smart Offer read authority rejected foreign or incomplete stats evidence.')
    }

    return stats
}

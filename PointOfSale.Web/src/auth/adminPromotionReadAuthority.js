const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const DISCOUNT_TYPES = new Set(['Percentage', 'FixedAmount', 0, 1])
const SCOPES = new Set(['Product', 'Category', 'Cart', 0, 1, 2])
const STATUSES = new Set(['Active', 'Inactive', 'Scheduled', 'Expired'])

const normalizeString = (value) => (typeof value === 'string' ? value.trim() : '')
const normalizeGuid = (value) => normalizeString(value)
const normalizeCurrency = (value) => normalizeString(value).toUpperCase()
const isFiniteNumber = (value) => Number.isFinite(Number(value))
const isOptionalFiniteNumber = (value) => value == null || isFiniteNumber(value)
const isOptionalInteger = (value) => value == null || Number.isInteger(Number(value))
const isDate = (value) => Number.isFinite(Date.parse(value))
const isOptionalDate = (value) => value == null || value === '' || isDate(value)

export const requireAdminPromotionReadAuthority = (promotion, currentUser, expectedPromotionId = null) => {
    if (!promotion || typeof promotion !== 'object' || Array.isArray(promotion)) {
        throw new Error('Promotion read authority rejected malformed promotion DTO evidence.')
    }

    const userTenantId = normalizeGuid(currentUser?.tenantId)
    const tenantId = normalizeGuid(promotion.tenantId ?? promotion.TenantId)
    const promotionId = normalizeGuid(promotion.id ?? promotion.Id)
    const expectedId = expectedPromotionId == null ? null : normalizeGuid(expectedPromotionId)
    const name = normalizeString(promotion.name ?? promotion.Name)
    const discountType = promotion.discountType ?? promotion.DiscountType
    const discountValue = promotion.discountValue ?? promotion.DiscountValue
    const currency = normalizeCurrency(promotion.currency ?? promotion.Currency)
    const rowVersion = normalizeString(promotion.rowVersion ?? promotion.RowVersion)
    const scope = promotion.scope ?? promotion.Scope
    const status = normalizeString(promotion.status ?? promotion.Status)
    const eligibilitySummary = normalizeString(promotion.eligibilitySummary ?? promotion.EligibilitySummary)

    if (!GUID_PATTERN.test(userTenantId) ||
        !GUID_PATTERN.test(tenantId) ||
        tenantId.toLowerCase() !== userTenantId.toLowerCase() ||
        !GUID_PATTERN.test(promotionId) ||
        (expectedId && promotionId.toLowerCase() !== expectedId.toLowerCase())) {
        throw new Error('Promotion read authority rejected foreign or route-mismatched promotion evidence.')
    }

    if (!name ||
        !DISCOUNT_TYPES.has(discountType) ||
        !isFiniteNumber(discountValue) ||
        !currency ||
        !rowVersion ||
        !Number.isInteger(Number(rowVersion)) ||
        Number(rowVersion) <= 0 ||
        !SCOPES.has(scope) ||
        !STATUSES.has(status) ||
        !eligibilitySummary ||
        !isDate(promotion.startDate ?? promotion.StartDate) ||
        !isDate(promotion.endDate ?? promotion.EndDate) ||
        !isOptionalFiniteNumber(promotion.maxDiscountAmount ?? promotion.MaxDiscountAmount) ||
        !isOptionalFiniteNumber(promotion.minOrderAmount ?? promotion.MinOrderAmount) ||
        !isOptionalInteger(promotion.priority ?? promotion.Priority) ||
        !isOptionalInteger(promotion.maxUsage ?? promotion.MaxUsage) ||
        !isOptionalInteger(promotion.currentUsage ?? promotion.CurrentUsage) ||
        !isOptionalDate(promotion.createdAt ?? promotion.CreatedAt) ||
        !isOptionalDate(promotion.updatedAt ?? promotion.UpdatedAt)) {
        throw new Error('Promotion read authority rejected incomplete promotion DTO evidence.')
    }

    return promotion
}

export const requireAdminPromotionListReadAuthority = (promotions, currentUser) => {
    if (!Array.isArray(promotions)) {
        throw new Error('Promotion read authority rejected malformed promotion list evidence.')
    }

    const seenPromotionIds = new Set()
    return promotions.map((promotion) => {
        const acceptedPromotion = requireAdminPromotionReadAuthority(promotion, currentUser)
        const promotionId = normalizeGuid(acceptedPromotion.id ?? acceptedPromotion.Id).toLowerCase()

        if (seenPromotionIds.has(promotionId)) {
            throw new Error('Promotion read authority rejected duplicate promotion list evidence.')
        }

        seenPromotionIds.add(promotionId)
        return acceptedPromotion
    })
}

export const requireAdminPromotionStatsReadAuthority = (stats, currentUser) => {
    if (!stats || typeof stats !== 'object' || Array.isArray(stats)) {
        throw new Error('Promotion read authority rejected malformed promotion stats evidence.')
    }

    const userTenantId = normalizeGuid(currentUser?.tenantId)
    const tenantId = normalizeGuid(stats.tenantId ?? stats.TenantId)
    const currency = normalizeCurrency(stats.currency ?? stats.Currency)

    if (!GUID_PATTERN.test(userTenantId) ||
        !GUID_PATTERN.test(tenantId) ||
        tenantId.toLowerCase() !== userTenantId.toLowerCase() ||
        !currency) {
        throw new Error('Promotion read authority rejected foreign promotion stats evidence.')
    }

    return stats
}

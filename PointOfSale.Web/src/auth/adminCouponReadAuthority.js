const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const DISCOUNT_TYPES = new Set(['Percentage', 'FixedAmount', 0, 1])
const STATUSES = new Set(['Active', 'Inactive', 'Expired'])

const normalizeString = (value) => (typeof value === 'string' ? value.trim() : '')
const normalizeGuid = (value) => normalizeString(value)
const normalizeCurrency = (value) => normalizeString(value).toUpperCase()
const isFiniteNumber = (value) => Number.isFinite(Number(value))
const isOptionalFiniteNumber = (value) => value == null || isFiniteNumber(value)
const isOptionalInteger = (value) => value == null || Number.isInteger(Number(value))
const isOptionalDate = (value) => {
    if (value == null || value === '') {
        return true
    }

    return Number.isFinite(Date.parse(value))
}

export const requireAdminCouponReadAuthority = (coupon, currentUser, expectedCouponId = null) => {
    if (!coupon || typeof coupon !== 'object' || Array.isArray(coupon)) {
        throw new Error('Coupon read authority rejected malformed coupon DTO evidence.')
    }

    const userTenantId = normalizeGuid(currentUser?.tenantId)
    const tenantId = normalizeGuid(coupon.tenantId ?? coupon.TenantId)
    const couponId = normalizeGuid(coupon.id ?? coupon.Id)
    const expectedId = expectedCouponId == null ? null : normalizeGuid(expectedCouponId)
    const code = normalizeString(coupon.code ?? coupon.Code)
    const name = normalizeString(coupon.name ?? coupon.Name)
    const discountType = coupon.discountType ?? coupon.DiscountType
    const discountValue = coupon.discountValue ?? coupon.DiscountValue
    const currency = normalizeCurrency(coupon.currency ?? coupon.Currency)
    const rowVersion = normalizeString(coupon.rowVersion ?? coupon.RowVersion)
    const status = normalizeString(coupon.status ?? coupon.Status)
    const eligibilitySummary = normalizeString(coupon.eligibilitySummary ?? coupon.EligibilitySummary)

    if (!GUID_PATTERN.test(userTenantId) ||
        !GUID_PATTERN.test(tenantId) ||
        tenantId.toLowerCase() !== userTenantId.toLowerCase() ||
        !GUID_PATTERN.test(couponId) ||
        (expectedId && couponId.toLowerCase() !== expectedId.toLowerCase())) {
        throw new Error('Coupon read authority rejected foreign or route-mismatched coupon evidence.')
    }

    if (!code ||
        !name ||
        !DISCOUNT_TYPES.has(discountType) ||
        !isFiniteNumber(discountValue) ||
        !currency ||
        !rowVersion ||
        !Number.isInteger(Number(rowVersion)) ||
        Number(rowVersion) <= 0 ||
        !STATUSES.has(status) ||
        !eligibilitySummary ||
        !isOptionalFiniteNumber(coupon.minOrderAmount ?? coupon.MinOrderAmount) ||
        !isOptionalFiniteNumber(coupon.maxDiscountAmount ?? coupon.MaxDiscountAmount) ||
        !isOptionalInteger(coupon.maxUsage ?? coupon.MaxUsage) ||
        !isOptionalInteger(coupon.currentUsage ?? coupon.CurrentUsage) ||
        !isOptionalInteger(coupon.maxUsagePerCustomer ?? coupon.MaxUsagePerCustomer) ||
        !isOptionalDate(coupon.startDate ?? coupon.StartDate) ||
        !isOptionalDate(coupon.endDate ?? coupon.EndDate)) {
        throw new Error('Coupon read authority rejected incomplete coupon DTO evidence.')
    }

    return coupon
}

export const requireAdminCouponListReadAuthority = (coupons, currentUser) => {
    if (!Array.isArray(coupons)) {
        throw new Error('Coupon read authority rejected malformed coupon list evidence.')
    }

    const seenCouponIds = new Set()
    return coupons.map((coupon) => {
        const acceptedCoupon = requireAdminCouponReadAuthority(coupon, currentUser)
        const couponId = normalizeGuid(acceptedCoupon.id ?? acceptedCoupon.Id).toLowerCase()

        if (seenCouponIds.has(couponId)) {
            throw new Error('Coupon read authority rejected duplicate coupon list evidence.')
        }

        seenCouponIds.add(couponId)
        return acceptedCoupon
    })
}

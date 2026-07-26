export interface CommissionBreakdown {
    price: number
    rate: number
    commissionAmount: number
    vendorReceives: number
}

export interface TargetEarningsPreview {
    desiredEarnings: number
    rate: number
    requiredPrice: number
}

const CURRENCY_SCALE = 2
const RATE_SCALE = 6

function roundHalfAwayFromZero(value: number, scale: number): number {
    if (!Number.isFinite(value)) {
        return 0
    }

    const absolute = Math.abs(value)
    const shifted = Number(`${absolute}e${scale}`)
    const rounded = Number(`${Math.round(shifted)}e-${scale}`)

    return value < 0 ? -rounded : rounded
}

export function roundCurrencyAmount(value: number): number {
    return roundHalfAwayFromZero(value, CURRENCY_SCALE)
}

export function roundCommissionRate(value: number): number {
    return roundHalfAwayFromZero(value, RATE_SCALE)
}

export function normalizePrice(value: number | null | undefined): number {
    if (!Number.isFinite(value ?? NaN)) {
        return 0
    }

    return Math.max(0, Number(value))
}

export function calculateCommissionBreakdown(
    priceInput: number | null | undefined,
    rateInput: number | null | undefined,
): CommissionBreakdown {
    const price = normalizePrice(priceInput)
    const rate = roundCommissionRate(Math.max(0, Number(rateInput ?? 0)))
    const commissionAmount = roundCurrencyAmount(price * rate)
    const vendorReceives = roundCurrencyAmount(Math.max(0, price - commissionAmount))

    return {
        price: roundCurrencyAmount(price),
        rate,
        commissionAmount,
        vendorReceives,
    }
}

export function calculateRequiredPriceForTargetEarnings(
    desiredEarningsInput: number | null | undefined,
    rateInput: number | null | undefined,
): TargetEarningsPreview | null {
    const desiredEarnings = normalizePrice(desiredEarningsInput)
    const rate = roundCommissionRate(Math.max(0, Number(rateInput ?? 0)))

    if (rate >= 1) {
        return null
    }

    const requiredPrice = roundCurrencyAmount(desiredEarnings / (1 - rate))

    return {
        desiredEarnings: roundCurrencyAmount(desiredEarnings),
        rate,
        requiredPrice,
    }
}

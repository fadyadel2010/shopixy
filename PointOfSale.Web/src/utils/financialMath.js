// IMPORTANT:
// This financial engine assumes 2 decimal places (scale = 100).
// Do NOT use currencies like JPY (0 decimals) or KWD (3 decimals)
// without refactoring the entire calculation pipeline.
const CURRENCY_SYMBOL_TO_CODE_MAP = {
    '$': 'USD',
    '\u20AC': 'EUR',
    '\u062C.\u0645': 'EGP',
}

const buildMinorUnitMap = () => {
    if (typeof Intl.supportedValuesOf !== 'function') {
        return new Map([
            ['USD', 2],
            ['EUR', 2],
            ['EGP', 2],
        ])
    }

    return new Map(
        Intl.supportedValuesOf('currency').map((currencyCode) => [
            currencyCode,
            new Intl.NumberFormat('en', {
                style: 'currency',
                currency: currencyCode,
            }).resolvedOptions().maximumFractionDigits,
        ]),
    )
}

const MINOR_UNIT_MAP = buildMinorUnitMap()

export const normalizeCurrencyCode = (currencyCode) => {
    const rawCurrencyCode = typeof currencyCode === 'string'
        ? currencyCode.trim()
        : ''

    if (!rawCurrencyCode) {
        return ''
    }

    const mappedCurrencyCode = CURRENCY_SYMBOL_TO_CODE_MAP[rawCurrencyCode] || rawCurrencyCode
    return mappedCurrencyCode.trim().toUpperCase()
}

export const assertTwoDecimalCurrency = (currencyCode) => {
    const normalizedCurrencyCode = normalizeCurrencyCode(currencyCode)
    const minorUnits = MINOR_UNIT_MAP.get(normalizedCurrencyCode)

    if (minorUnits === undefined) {
        throw new Error(
            `Unknown currency "${currencyCode}". Currency not recognized by the system.`,
        )
    }

    if (minorUnits !== 2) {
        throw new Error(
            `Unsupported currency "${currencyCode}" (${minorUnits} decimals). Only 2-decimal currencies are supported in this financial engine.`,
        )
    }

    return normalizedCurrencyCode
}

export const toSafeNumber = (value, fallback = 0) => {
    if (typeof value === 'number') {
        return Number.isFinite(value) ? value : fallback
    }

    if (typeof value !== 'string') {
        return fallback
    }

    const trimmedValue = value.trim()
    if (!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(trimmedValue)) {
        return fallback
    }

    const parsed = Number(trimmedValue)
    return Number.isFinite(parsed) ? parsed : fallback
}

const buildInvalidAmountParse = (reason, rawValue = '') => ({
    isValid: false,
    value: null,
    rawValue,
    normalizedValue: '',
    reason,
})

export const parseStrictAmountInput = (
    value,
    {
        allowZero = true,
        allowNegative = false,
        maxDecimalPlaces = 2,
    } = {},
) => {
    if (value == null) {
        return buildInvalidAmountParse('required')
    }

    const rawValue = typeof value === 'number'
        ? String(value)
        : typeof value === 'string'
            ? value
            : ''
    const normalizedValue = rawValue.trim()

    if (!normalizedValue) {
        return buildInvalidAmountParse('required', rawValue)
    }

    if (normalizedValue.includes(',')) {
        return buildInvalidAmountParse('invalid_format', rawValue)
    }

    if (!allowNegative && normalizedValue.startsWith('-')) {
        return buildInvalidAmountParse('negative', rawValue)
    }

    const signPattern = allowNegative ? '-?' : ''
    const amountPattern = new RegExp(`^${signPattern}(?:\\d+|\\d*\\.\\d+)$`)
    if (!amountPattern.test(normalizedValue)) {
        return buildInvalidAmountParse('invalid_format', rawValue)
    }

    const decimalPart = normalizedValue.split('.')[1] || ''
    if (decimalPart.length > maxDecimalPlaces) {
        return buildInvalidAmountParse('too_many_decimals', rawValue)
    }

    const parsed = Number(normalizedValue)
    if (!Number.isFinite(parsed)) {
        return buildInvalidAmountParse('invalid_format', rawValue)
    }

    if (!allowZero && parsed === 0) {
        return buildInvalidAmountParse('zero', rawValue)
    }

    return {
        isValid: true,
        value: parsed,
        rawValue,
        normalizedValue,
        reason: null,
    }
}

const roundHalfAwayFromZero = (value, scale = 2) => {
    const safeValue = toSafeNumber(value)
    if (safeValue === 0) {
        return 0
    }

    const sign = Math.sign(safeValue)
    const absoluteValue = Math.abs(safeValue)
    const factor = 10 ** scale
    const roundedAbsolute = Math.round(
        (absoluteValue + Number.EPSILON * Math.max(1, absoluteValue)) * factor,
    )

    return sign * roundedAbsolute
}

export const roundCurrency = (value) => {
    return roundHalfAwayFromZero(value, 2) / 100
}

export const toCurrencyMinorUnits = (value) => roundHalfAwayFromZero(value, 2)

export const fromCurrencyMinorUnits = (value) => toSafeNumber(value) / 100

export const formatCurrencyInputValue = (value) => {
    const minorUnits = toCurrencyMinorUnits(value)
    const sign = minorUnits < 0 ? '-' : ''
    const absoluteMinorUnits = Math.abs(minorUnits)
    const wholeUnits = Math.floor(absoluteMinorUnits / 100)
    const fractionalUnits = String(absoluteMinorUnits % 100).padStart(2, '0')

    return `${sign}${wholeUnits}.${fractionalUnits}`
}

export const clampAmount = (value, min, max) => (
    Math.min(toSafeNumber(max), Math.max(toSafeNumber(min), toSafeNumber(value)))
)

export const divide = (a, b) => {
    const dividend = toSafeNumber(a)
    const divisor = toSafeNumber(b)
    if (divisor === 0) {
        return 0
    }
    return dividend / divisor
}

export const sumCurrency = (values) => (
    fromCurrencyMinorUnits(
        (Array.isArray(values) ? values : []).reduce(
            (sum, value) => sum + toCurrencyMinorUnits(value),
            0,
        ),
    )
)

export const calculateLineSubtotal = (quantity, unitPrice) => (
    roundCurrency(Math.max(0, toSafeNumber(quantity)) * Math.max(0, toSafeNumber(unitPrice)))
)

export const calculatePercentageAmount = (amount, rate) => (
    roundCurrency(Math.max(0, toSafeNumber(amount)) * Math.max(0, toSafeNumber(rate)) / 100)
)

export const calculateLineTax = (quantity, unitPrice, taxRate) => (
    calculatePercentageAmount(calculateLineSubtotal(quantity, unitPrice), taxRate)
)

export const calculateLineTotal = (quantity, unitPrice, taxRate) => (
    sumCurrency([
        calculateLineSubtotal(quantity, unitPrice),
        calculateLineTax(quantity, unitPrice, taxRate),
    ])
)

export const getEffectiveUnitPrice = (item) => {
    const currentUnitPrice = toSafeNumber(item?.currentUnitPrice, Number.NaN)

    if (item?.isStalePrice && Number.isFinite(currentUnitPrice)) {
        return Math.max(0, currentUnitPrice)
    }

    return Math.max(0, toSafeNumber(item?.unitPrice))
}

export const clampLineDiscount = (quantity, unitPrice, discountAmount) => (
    clampAmount(Math.max(0, roundCurrency(discountAmount)), 0, calculateLineSubtotal(quantity, unitPrice))
)

export const getClampedLineDiscountState = (quantity, unitPrice, discountAmount) => {
    const clampedDiscountAmount = clampLineDiscount(quantity, unitPrice, discountAmount)
    return {
        discountAmount: clampedDiscountAmount,
        wasAdjusted: clampedDiscountAmount !== roundCurrency(discountAmount),
    }
}

export const calculateRemainingSubtotal = (subtotal, itemDiscounts) => (
    Math.max(0, sumCurrency([subtotal, -itemDiscounts]))
)

export const calculateRemainingSubtotalFromItems = (items) => (
    calculateRemainingSubtotal(
        sumCurrency((Array.isArray(items) ? items : []).map(
            item => calculateLineSubtotal(item.quantity, getEffectiveUnitPrice(item)),
        )),
        sumCurrency((Array.isArray(items) ? items : []).map(
            item => clampLineDiscount(item.quantity, getEffectiveUnitPrice(item), item.discountAmount),
        )),
    )
)

// Mirrors the assigned ISO codes in PointOfSale.Core/Helpers/CountryCodeHelper.cs.
// The parity test guards this browser list against drift from that authority.
// Intl.DisplayNames is used only for labels, not validation (it accepts non-ISO regions).
export const SHIPPING_METHOD_COUNTRY_CODES = Object.freeze(`
AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ
BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ
CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ
DE DJ DK DM DO DZ
EC EE EG EH ER ES ET
FI FJ FK FM FO FR
GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY
HK HM HN HR HT HU
ID IE IL IM IN IO IQ IR IS IT
JE JM JO JP
KE KG KH KI KM KN KP KR KW KY KZ
LA LB LC LI LK LR LS LT LU LV LY
MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ
NA NC NE NF NG NI NL NO NP NR NU NZ
OM
PA PE PF PG PH PK PL PM PN PR PS PT PW PY
QA
RE RO RS RU RW
SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ
TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ
UA UG UM US UY UZ
VA VC VE VG VI VN VU
WF WS
YE YT
ZA ZM ZW
`.trim().split(/\s+/))

const countryCodes = new Set(SHIPPING_METHOD_COUNTRY_CODES)

function invalidScope() {
    const error = new Error('Shipping method country scope must be null or a non-empty JSON array of unique assigned ISO 3166-1 alpha-2 codes.')
    error.code = 'INVALID_SHIPPING_COUNTRY_SCOPE'
    return error
}

function validateCodes(codes) {
    if (!Array.isArray(codes) || codes.length === 0
        || codes.some((code) => typeof code !== 'string' || !countryCodes.has(code))
        || new Set(codes).size !== codes.length) {
        throw invalidScope()
    }
    return codes
}

// The API omits null properties. Only null/omitted means all countries; malformed
// strings, JSON null and empty arrays must never become an unrestricted scope.
export function parseShippingMethodCountryScope(value) {
    if (value === null || value === undefined) return null
    if (typeof value !== 'string') throw invalidScope()
    let parsed
    try {
        parsed = JSON.parse(value)
    } catch {
        throw invalidScope()
    }
    return validateCodes(parsed)
}

export function serializeShippingMethodCountryScope(codes) {
    return codes === null ? null : JSON.stringify(validateCodes(codes))
}

export function getShippingMethodCountryOptions(language) {
    const names = typeof Intl.DisplayNames === 'function'
        ? new Intl.DisplayNames([language === 'ar' ? 'ar' : 'en'], { type: 'region' })
        : null
    return SHIPPING_METHOD_COUNTRY_CODES.map((code) => ({
        code,
        label: `${names?.of(code) || code} (${code})`,
    }))
}

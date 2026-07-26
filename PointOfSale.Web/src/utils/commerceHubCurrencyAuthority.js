export const COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'

export const normalizeCommerceHubCurrency = (currency) =>
    String(currency || '').trim().toUpperCase()

export const isCommerceHubAuthorityCurrency = (currency) =>
    normalizeCommerceHubCurrency(currency) === COMMERCEHUB_AUTHORITY_CURRENCY

export const resolveCommerceHubDisplayCurrency = (currency) =>
    isCommerceHubAuthorityCurrency(currency) ? COMMERCEHUB_AUTHORITY_CURRENCY : null

export const COMMERCEHUB_UNSUPPORTED_CURRENCY_LABEL = 'CommerceHub currency blocked'

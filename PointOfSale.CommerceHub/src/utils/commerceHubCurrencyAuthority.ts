export const COMMERCEHUB_AUTHORITY_CURRENCY = "EGP" as const;

export const normalizeCommerceHubCurrency = (currency?: string | null) =>
  (currency ?? "").trim().toUpperCase();

export const isCommerceHubAuthorityCurrency = (currency?: string | null) =>
  normalizeCommerceHubCurrency(currency) === COMMERCEHUB_AUTHORITY_CURRENCY;

export const resolveCommerceHubDisplayCurrency = (currency?: string | null) =>
  isCommerceHubAuthorityCurrency(currency) ? COMMERCEHUB_AUTHORITY_CURRENCY : null;

export const COMMERCEHUB_UNSUPPORTED_CURRENCY_LABEL = "CommerceHub currency blocked";

/**
 * Catalog DTO currency is evidence, never a formatting preference.  Reject it
 * before it is admitted to a query cache or a presentation component.
 */
export const requireCommerceHubCatalogCurrency = (currency?: string | null) => {
  if (!isCommerceHubAuthorityCurrency(currency)) {
    throw new Error("CommerceHub catalog currency authority rejected non-EGP currency evidence.");
  }

  return COMMERCEHUB_AUTHORITY_CURRENCY;
};

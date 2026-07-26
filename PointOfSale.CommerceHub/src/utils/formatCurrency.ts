import {
  COMMERCEHUB_AUTHORITY_CURRENCY,
  COMMERCEHUB_UNSUPPORTED_CURRENCY_LABEL,
  resolveCommerceHubDisplayCurrency
} from "./commerceHubCurrencyAuthority";

type FormatCurrencyOptions = {
  locale?: string;
  minimumFractionDigits?: number;
  maximumFractionDigits?: number;
};

export const formatCurrency = (
  value: number,
  currency: string = COMMERCEHUB_AUTHORITY_CURRENCY,
  options: FormatCurrencyOptions = {}
) => {
  const normalizedCurrency = resolveCommerceHubDisplayCurrency(currency);
  if (!normalizedCurrency) {
    return COMMERCEHUB_UNSUPPORTED_CURRENCY_LABEL;
  }

  const normalizedValue = Number.isFinite(value) ? value : 0;
  const fractionDigits = Number.isInteger(normalizedValue) ? 0 : 2;

  return new Intl.NumberFormat(options.locale ?? "en-EG", {
    style: "currency",
    currency: normalizedCurrency,
    currencyDisplay: "code",
    minimumFractionDigits: options.minimumFractionDigits ?? fractionDigits,
    maximumFractionDigits: options.maximumFractionDigits ?? fractionDigits
  }).format(normalizedValue);
};

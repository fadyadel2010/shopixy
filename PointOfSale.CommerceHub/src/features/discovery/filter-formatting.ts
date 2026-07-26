const compactUppercaseTokens = new Set([
  "gb",
  "tb",
  "mb",
  "ram",
  "ssd",
  "hdd",
  "uhd",
  "hd",
  "tv",
  "xs",
  "xl",
  "xxl"
]);

export const formatFilterLabel = (value: string) =>
  value
    .replace(/[_-]+/g, " ")
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .map((token) => {
      const normalized = token.toLowerCase();
      if (compactUppercaseTokens.has(normalized) || /^\d+[a-z]{1,4}$/i.test(token)) {
        return token.toUpperCase();
      }

      return token.charAt(0).toUpperCase() + token.slice(1);
    })
    .join(" ");

export const formatRangeLabel = (min?: number, max?: number, formatter?: Intl.NumberFormat) => {
  const numberFormatter = formatter ?? new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 });

  if (min !== undefined && max !== undefined) {
    return `${numberFormatter.format(min)} - ${numberFormatter.format(max)}`;
  }

  if (min !== undefined) {
    return `From ${numberFormatter.format(min)}`;
  }

  if (max !== undefined) {
    return `Up to ${numberFormatter.format(max)}`;
  }

  return "";
};

export const SHIPPING_METHOD_COUNTRY_CODES: readonly string[];
export function parseShippingMethodCountryScope(value: unknown): string[] | null;
export function serializeShippingMethodCountryScope(codes: readonly string[] | null): string | null;
export function getShippingMethodCountryOptions(language: string): Array<{ code: string; label: string }>;

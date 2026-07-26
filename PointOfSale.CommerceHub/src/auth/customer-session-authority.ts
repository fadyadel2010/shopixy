import type { CustomerAuthResponse, CustomerNavigationCapability, CustomerProfile } from "@/types";

export const CUSTOMER_SESSION_LOGOUT_STORAGE_KEY = "commercehub_customer_logout_at";
export const CUSTOMER_SESSION_AUTHORITY_LOST_EVENT = "commercehub:customer-session-authority-lost";

const CUSTOMER_SESSION_AUTHORITY_ENDPOINT_PREFIXES = [
  "/api/commercehub/checkout",
  "/api/commercehub/payments/initiate",
  "/api/customers/orders",
  "/api/customers/refunds",
  "/api/customers/payout-details",
  "/api/marketplace/reviews",
  "/api/customers/auth/me",
  "/api/customers/auth/profile",
  "/api/customers/auth/change-password",
  "/api/customers/auth/change-email",
  "/api/customers/auth/revoke-all-sessions"
];

const CUSTOMER_SESSION_AUTHORITY_ENDPOINT_SUFFIXES = [
  "/review-context"
];

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === "object" && value !== null && !Array.isArray(value);

const isNonEmptyString = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;

const isValidIsoDate = (value: unknown): value is string => isNonEmptyString(value) && Number.isFinite(Date.parse(value));

const isValidGuid = (value: unknown): value is string =>
  isNonEmptyString(value) &&
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);

const isValidEmail = (value: unknown): value is string =>
  isNonEmptyString(value) &&
  /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);

export const CUSTOMER_NAVIGATION_CAPABILITIES = new Set<CustomerNavigationCapability>([
  "customer.account.view",
  "customer.account.settings",
  "customer.orders.view",
  "customer.refunds.view"
]);

const deriveCustomerNavigationCapabilities = (value: unknown): CustomerNavigationCapability[] | null => {
  if (!Array.isArray(value) || value.length === 0) {
    return null;
  }

  const capabilities = value.filter(
    (capability): capability is CustomerNavigationCapability =>
      typeof capability === "string" &&
      CUSTOMER_NAVIGATION_CAPABILITIES.has(capability as CustomerNavigationCapability)
  );

  if (capabilities.length !== value.length || new Set(capabilities).size !== capabilities.length) {
    return null;
  }

  return capabilities;
};

export const deriveCustomerSessionProfileAuthority = (value: unknown): CustomerProfile | null => {
  if (!isRecord(value)) {
    return null;
  }

  const { id, email, name, phoneNumber, createdAt } = value;
  const navigationCapabilities = deriveCustomerNavigationCapabilities(value.navigationCapabilities);

  if (
    !isValidGuid(id) ||
    !isValidEmail(email) ||
    !isNonEmptyString(name) ||
    value.isEmailVerified !== true ||
    !isValidIsoDate(createdAt) ||
    !navigationCapabilities
  ) {
    return null;
  }

  if (phoneNumber !== undefined && phoneNumber !== null && typeof phoneNumber !== "string") {
    return null;
  }

  return {
    id,
    email,
    name,
    phoneNumber: phoneNumber ?? null,
    isEmailVerified: true,
    createdAt,
    navigationCapabilities
  };
};

export const customerHasNavigationCapability = (
  customer: CustomerProfile | null | undefined,
  capability: CustomerNavigationCapability
) => customer?.navigationCapabilities.includes(capability) === true;

export const isSameCustomerActorAuthority = (
  left: CustomerProfile,
  right: CustomerProfile
) => left.id === right.id && left.email.toLowerCase() === right.email.toLowerCase();

export const deriveCustomerAuthResponseAuthority = (value: unknown): CustomerAuthResponse | null => {
  if (!isRecord(value) || !isValidIsoDate(value.expiresAt) || Date.parse(value.expiresAt) <= Date.now()) {
    return null;
  }

  const customer = deriveCustomerSessionProfileAuthority(value.customer);
  if (!customer) {
    return null;
  }

  return {
    expiresAt: value.expiresAt,
    customer
  };
};

export const isCustomerSessionAuthorityEndpoint = (url: string | undefined) => {
  if (!url) {
    return false;
  }

  let path = url;
  try {
    path = new URL(url, window.location.origin).pathname;
  } catch {
    path = url.split("?")[0] ?? url;
  }

  return (
    CUSTOMER_SESSION_AUTHORITY_ENDPOINT_PREFIXES.some((prefix) => path.startsWith(prefix)) ||
    CUSTOMER_SESSION_AUTHORITY_ENDPOINT_SUFFIXES.some((suffix) => path.endsWith(suffix))
  );
};

export const dispatchCustomerSessionAuthorityLost = () => {
  try {
    window.dispatchEvent(new Event(CUSTOMER_SESSION_AUTHORITY_LOST_EVENT));
  } catch {
    // Backend authority loss is also enforced by the failed request itself.
  }
};

export const broadcastCustomerLogout = () => {
  try {
    window.localStorage.setItem(CUSTOMER_SESSION_LOGOUT_STORAGE_KEY, new Date().toISOString());
  } catch {
    // Storage is an optional revocation signal; backend revalidation remains authoritative.
  }
};

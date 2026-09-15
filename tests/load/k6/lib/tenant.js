// M1.1 tenant context helpers.
// Authority model (discovery section 5): server-side TenantResolutionMiddleware
// strips inbound X-Tenant-Id and re-resolves from claim/domain/host/slug.
// The harness SENDS X-Tenant-Id where the API expects it but NEVER treats a
// response DTO TenantId as authority — expected identity always comes from
// the deterministic VU mapping / explicit env config.

import { identityForVu } from './identity.js';

// Explicit env-driven tenant config for smoke.js. Falls back to VU mapping
// only when env is absent; smoke.js itself requires the env vars.
export function tenantConfigFromEnv(env) {
  return {
    tenantId: env.TEST_TENANT || '',
    slug: env.TEST_SLUG || '',
    productId: env.TEST_PRODUCT_ID || '',
    storeId: env.TEST_TENANT || '', // Tenant == Store
  };
}

export function tenantConfigForVu(vuId) {
  var id = identityForVu(vuId);
  return {
    tenantId: id.tenantId,
    slug: id.storeSlug,
    storeId: id.storeId,
    tenantKey: id.tenantKey,
  };
}

// Headers required by the discovered API: X-Tenant-Id for tenant scoping,
// X-Session-Id for guest cart/checkout flows (harmless on reads).
export function tenantHeaders(tenantId, sessionId) {
  var headers = {};
  if (tenantId) headers['X-Tenant-Id'] = tenantId;
  if (sessionId) headers['X-Session-Id'] = sessionId;
  return headers;
}

// Expected identity bundle used by checks.js — the ONLY authority for
// "which tenant should this response belong to".
export function expectedTenantIdentity(vuId, overrides) {
  var base = tenantConfigForVu(vuId);
  if (!overrides) return base;
  return {
    tenantId: overrides.tenantId || base.tenantId,
    slug: overrides.slug || base.slug,
    storeId: overrides.storeId || base.storeId,
    tenantKey: overrides.tenantKey || base.tenantKey,
  };
}

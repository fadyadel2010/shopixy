// M1.1 deterministic VU identity mapping only.
// No user creation, no authentication, no randomness in tenant selection.
// Mapping: VU 1 -> tenant-a, VU 2 -> tenant-b, VU 3 -> tenant-c, then cycles.
// See tests/load/LOAD_TEST_DISCOVERY.md section 7.

var TENANTS = [
  { index: 0, tenantKey: 'tenant-a', tenantId: '00000000-0000-5000-8000-00000000000a', slug: 'store-a', userKey: 'user-a-001', customerKey: 'customer-a-001' },
  { index: 1, tenantKey: 'tenant-b', tenantId: '00000000-0000-5000-8000-00000000000b', slug: 'store-b', userKey: 'user-b-001', customerKey: 'customer-b-001' },
  { index: 2, tenantKey: 'tenant-c', tenantId: '00000000-0000-5000-8000-00000000000c', slug: 'store-c', userKey: 'user-c-001', customerKey: 'customer-c-001' },
];

function vuNumber(vuId) {
  var n = parseInt(vuId, 10);
  if (isNaN(n) || n < 1) return 1;
  return n;
}

// 1-based VU id -> deterministic tenant slot (cycles every 3).
export function identityForVu(vuId) {
  var n = vuNumber(vuId);
  var slot = TENANTS[(n - 1) % TENANTS.length];
  return {
    vuId: String(n),
    tenantKey: slot.tenantKey,
    tenantId: slot.tenantId,
    storeSlug: slot.slug,
    storeId: slot.tenantId, // Tenant == Store by convention (discovery section 5.2)
    userKey: slot.userKey,
    customerKey: slot.customerKey,
    sessionId: 'sess-' + slot.tenantKey + '-vu-' + String(n),
    vuKind: 'smoke',
  };
}

export function tenantIdForVu(vuId) {
  return identityForVu(vuId).tenantId;
}

export function storeSlugForVu(vuId) {
  return identityForVu(vuId).storeSlug;
}

export function userKeyForVu(vuId) {
  return identityForVu(vuId).userKey;
}

export function sessionIdForVu(vuId, iteration) {
  var base = identityForVu(vuId).sessionId;
  if (iteration === undefined || iteration === null) return base;
  return base + '-iter-' + String(iteration);
}

// UUIDv4-like request id. Uses Math.random (per-request uniqueness is the
// requirement; determinism applies to VU->tenant mapping, not request ids).
// Format only; not cryptographically strong — correlation use only.
export function newRequestId() {
  function h4() {
    return Math.floor(Math.random() * 0xffff).toString(16).padStart(4, '0');
  }
  return h4() + h4() + '-' + h4() + '-4' + h4().substr(1) + '-a' + h4().substr(1) + '-' + h4() + h4() + h4();
}

export function __testOnlyTenants() {
  return TENANTS;
}

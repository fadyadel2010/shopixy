// M4 manifest helpers: frozen-manifest consumption + deterministic VU mapping.
// The manifest (tests/load/data/manifests/latest.json) is the ONLY source of
// truth for tenant/product/probe identity. Nothing here duplicates IDs and
// nothing here performs I/O: the main script loads the file with open() and
// passes the parsed object in (open() resolves relative to the main script).

export var M4_REQUIRED_FLAGS = [
  'projectionReady',
  'publicationReady',
  'mongoVerified',
  'elasticsearchVerified',
  'storefrontVerified',
  'isolationProbesVerified',
  'stockBudgetVerified',
];

// Fail-closed validation. Returns { ok, failures[] }.
export function validateManifest(m) {
  var failures = [];
  if (!m || typeof m !== 'object') return { ok: false, failures: ['manifest is not an object'] };
  for (var i = 0; i < M4_REQUIRED_FLAGS.length; i++) {
    var k = M4_REQUIRED_FLAGS[i];
    if (m[k] !== true) failures.push('required flag not true: ' + k + ' (was ' + JSON.stringify(m[k]) + ')');
  }
  if (!Array.isArray(m.tenants) || m.tenants.length < 3) failures.push('tenants array missing or < 3');
  if (!Array.isArray(m.probes) || m.probes.length < 3) failures.push('probes array missing or < 3');
  return { ok: failures.length === 0, failures: failures };
}

function tenantByKey(m, key) {
  for (var i = 0; i < m.tenants.length; i++) {
    if (m.tenants[i].key === key) return m.tenants[i];
  }
  return null;
}

function probeByTenantKey(m, tenantKey) {
  for (var i = 0; i < (m.probes || []).length; i++) {
    if (m.probes[i].tenantKey === tenantKey) return m.probes[i];
  }
  return null;
}

// Deterministic VU->tenant mapping shared by all M4 phases:
// VU 1 -> tenant-a, VU 2 -> tenant-b, VU 3 -> tenant-c, then cycles.
// M4.0 executes VU 1 only (tenant-a). Never random.
export function tenantForVu(m, vu) {
  var n = parseInt(vu, 10);
  if (isNaN(n) || n < 1) n = 1;
  var keys = ['a', 'b', 'c'];
  var key = keys[(n - 1) % keys.length];
  var t = tenantByKey(m, key);
  if (!t) throw new Error('M4 manifest has no tenant key=' + key);
  var probe = probeByTenantKey(m, 'tenant-' + key);
  if (!probe) throw new Error('M4 manifest has no probe for tenant-' + key);
  var normal = null;
  for (var i = 0; i < (t.products || []).length; i++) {
    if (t.products[i].kind === 'normal') { normal = t.products[i]; break; }
  }
  if (!normal) throw new Error('M4 manifest tenant-' + key + ' has no normal product');
  var sentinel = null;
  for (var j = 0; j < (t.products || []).length; j++) {
    if (t.products[j].kind === 'sentinel') { sentinel = t.products[j]; break; }
  }
  if (!sentinel) throw new Error('M4 manifest tenant-' + key + ' has no sentinel product');
  return {
    vu: n,
    key: key,
    tenantKey: 'tenant-' + key,
    tenantId: t.tenantId,
    slug: t.slug,
    storeId: t.tenantId, // Tenant == Store by convention (discovery section 5.2)
    probe: probe,
    normalProduct: normal,
    sentinelProduct: sentinel,
    tenantRecord: t,
  };
}

// Every identifier belonging to tenants OTHER than ownKey. Used for I-1
// foreign-presence scans. Multi-identifier (GUID, SKU, slug, barcode, price,
// category, customer) — never names alone.
export function foreignStringsFor(m, ownKey) {
  var out = [];
  function push(v) {
    if (v === null || v === undefined) return;
    var s = String(v);
    if (s.length < 2) return;
    if (out.indexOf(s) === -1) out.push(s);
  }
  var tenants = m.tenants || [];
  for (var i = 0; i < tenants.length; i++) {
    var t = tenants[i];
    if (t.key === ownKey) continue;
    push(t.tenantId);
    push(t.slug);
    var products = t.products || [];
    for (var p = 0; p < products.length; p++) {
      push(products[p].productId);
      push(products[p].slug);
      push(products[p].sku);
    }
    var cats = t.categories || [];
    for (var c = 0; c < cats.length; c++) {
      push(cats[c].categoryId);
      push(cats[c].name);
    }
    var staff = t.staff || [];
    for (var s = 0; s < staff.length; s++) push(staff[s].userId);
    var customers = t.customers || [];
    for (var u = 0; u < customers.length; u++) push(customers[u].customerId);
  }
  var probes = m.probes || [];
  for (var k = 0; k < probes.length; k++) {
    var pr = probes[k];
    if (pr.tenantKey === 'tenant-' + ownKey) continue;
    push(pr.probeProductId);
    push(pr.probeName);
    push(pr.probeSlug);
    push(pr.probeSku);
    push(pr.probeBarcode);
    push(pr.probePrice);
    push(pr.probeCategoryId);
    push(pr.probeCategoryName);
    push(pr.probeCustomerId);
    push(pr.probeCustomerEmail);
  }
  return out;
}

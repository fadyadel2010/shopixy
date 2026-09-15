// M1.1 read-only Stage-0 smoke: 1 VU, 1 iteration.
// Routes verified in tests/load/LOAD_TEST_DISCOVERY.md section 3.1.
// Read-only: GET only. No auth, no writes, no TestAuth, no secrets.
// Required env: BASE_URL, TEST_TENANT, TEST_SLUG, TEST_PRODUCT_ID.
import { check, fail } from 'k6';
import { getWithMetadata } from './lib/http.js';
import { identityForVu, sessionIdForVu } from './lib/identity.js';
import { assertNoForeignTenantProducts, assertStoreResolution } from './lib/checks.js';
import { SMOKE_THRESHOLDS } from './lib/thresholds.js';

export const options = {
  vus: 1,
  iterations: 1,
  thresholds: SMOKE_THRESHOLDS,
};

function env(name) {
  // k6 exposes __ENV (not process.env).
  return (__ENV[name] || '').trim();
}

function baseCtx(baseUrl, vuTag) {
  var id = identityForVu(vuTag);
  var tenantId = env('TEST_TENANT') || id.tenantId;
  var slug = env('TEST_SLUG') || id.storeSlug;
  return {
    vuId: String(vuTag),
    userKey: id.userKey,
    customerKey: id.customerKey,
    tenantId: tenantId,
    storeId: tenantId, // Tenant == Store
    slug: slug,
    storeSlug: slug,
    sessionId: sessionIdForVu(vuTag, __ITER),
    // M1.2: anonymous tenant resolution is Host-based (TenantResolutionMiddleware
    // extracts {slug} from Storefront:UrlTemplate, port-sensitive). The TCP
    // connection still targets BASE_URL; only the Host header carries the
    // storefront domain. Overridable via TEST_HOST_OVERRIDE.
    hostOverride: env('TEST_HOST_OVERRIDE') || (slug + '.localhost:5174'),
    expectedProbe: null, // set per-tenant probe name when fixtures exist
    tagsFor: function (endpoint) {
      return { tenant: tenantId, store: slug, profile: 'read-smoke', endpoint: endpoint, vu_kind: 'smoke' };
    },
  };
}

function step(url, ctx, endpoint, extraChecks) {
  var r = getWithMetadata(url, {
    tenantId: ctx.tenantId,
    sessionId: ctx.sessionId,
    hostOverride: ctx.hostOverride,
    tags: ctx.tagsFor(endpoint),
  });
  var ok = check(r.res, {
    'status is 2xx/4xx (never 5xx)': function (res) { return res.status >= 200 && res.status < 500; },
  });
  if (!ok) {
    console.error(JSON.stringify({ endpoint: endpoint, violation: 'non-2xx/4xx', meta: r.meta }));
  }
  if (extraChecks) extraChecks(r.res, r.meta);
  return r;
}

export default function () {
  var baseUrl = env('BASE_URL');
  var productId = env('TEST_PRODUCT_ID');
  var slug = env('TEST_SLUG');
  var tenant = env('TEST_TENANT');
  if (!baseUrl) fail('BASE_URL is required (e.g. http://localhost:5000)');
  if (!tenant) fail('TEST_TENANT is required (tenant GUID under test)');
  if (!slug) fail('TEST_SLUG is required (store slug under test)');
  if (!productId) fail('TEST_PRODUCT_ID is required (deterministic product id or slug)');

  var base = baseUrl.replace(/\/+$/, '');
  var ctx = baseCtx(baseUrl, __VU);

  // 1. Health (safe probe; tenant headers harmless here).
  step(base + '/health/ready', ctx, 'health-ready');

  // 2. Slug resolution (Redis cache-first read-only).
  var r2 = step(base + '/api/storefront/resolve-slug/' + encodeURIComponent(slug), ctx, 'resolve-slug');
  try {
    assertStoreResolution(JSON.parse(r2.res.body || '{}'), ctx.slug, ctx.tenantId, ctx, r2.meta);
  } catch (e) { /* non-JSON body: I-2 skipped, status check above still applies */ }

  // 3. Store config.
  step(base + '/api/storefront/store-config', ctx, 'store-config');

  // 4. Categories.
  step(base + '/api/storefront/categories', ctx, 'categories');

  // 5. Product listing.
  var r5 = step(base + '/api/storefront/products', ctx, 'products');
  try {
    assertNoForeignTenantProducts(JSON.parse(r5.res.body || '{}'), ctx.tenantId, ctx, r5.meta);
  } catch (e) { /* non-JSON body: I-1 skipped */ }

  // 6. Deterministic probe/product detail (by-id; identifier from env).
  var r6 = step(base + '/api/storefront/products/by-id/' + encodeURIComponent(productId), ctx, 'product-detail');
  try {
    assertNoForeignTenantProducts(JSON.parse(r6.res.body || '{}'), ctx.tenantId, ctx, r6.meta);
  } catch (e) { /* non-JSON body: I-1 skipped */ }

  // 7. Search.
  var r7 = step(base + '/api/storefront/search?q=probe', ctx, 'search');
  try {
    assertNoForeignTenantProducts(JSON.parse(r7.res.body || '{}'), ctx.tenantId, ctx, r7.meta);
  } catch (e) { /* non-JSON body: I-1 skipped */ }

  // 8. Suggest.
  step(base + '/api/storefront/search/suggest?q=pro', ctx, 'search-suggest');
}

// M4.0 DETERMINISTIC SINGLE-VU READ SCENARIO (READ-ONLY).
//
// HARD SCOPE: exactly 1 VU, 1 iteration. No multi-VU, no ramping, no stress,
// no spike/soak/breakpoint, no checkout/POS/sale/transactional writes.
// GET only (lib/http.js throws on any other method). No auth, no seeding,
// no Mongo/ES writes, no Redis clears, no provider/webhook/maintenance/
// debug/reindex/dev/test-auth calls.
//
// Manifest (tests/load/data/manifests/latest.json) is the source of truth:
// tenant/product/probe IDs come from it, never from hardcoded copies.
// setup() validates the frozen flags and REFUSES TO RUN on any failure.
//
// Route correction vs the M1 smoke design (verified against controllers +
// live M4 probes on 2026-09-15, see M4_DETERMINISTIC_READ_REPORT.md section 3):
// smoke.js resolved resolve-slug/{store-slug}; tenant slugs are NOT catalog
// slugs, so M4.0 resolves resolve-slug/{manifest probe product slug}
// (SlugResolverController: tries product first, then category; 200 with
// canonicalSlug match proven live). search/suggest paths were already
// correct (StorefrontSearchController [Route api/storefront/search] +
// [HttpGet suggest]). Step 9 uses GET /api/storefront/home/featured-products
// (StorefrontHomeController, discovery section 3.1 safe read; 200 proven
// live; returns [] since no featured flags are seeded).
//
// Run exactly:
//   k6 run --vus 1 --iterations 1 tests/load/k6/m4-deterministic-read.js
// Env: BASE_URL (default http://localhost:5010, the M3-B evidence URL),
//      M4_RUN_ID (default m4-local-run; stamped into every tag + bundle).

import { check, fail } from 'k6';
import { Trend, Counter } from 'k6/metrics';
import { getWithMetadata } from './lib/http.js';
import { newRequestId } from './lib/identity.js';
import { validateManifest, tenantForVu, foreignStringsFor } from './lib/manifest.js';

// --- frozen manifest, loaded once at init (source of truth) -----------------
var MANIFEST = JSON.parse(open('../data/manifests/latest.json'));

// --- metrics (recorded; NO production SLO claims) ----------------------------
var mHttpLatency = new Trend('m4_http_latency_ms');
var mJourney = new Trend('m4_journey_duration_ms');
var mFailed = new Counter('m4_failed_requests');
var mI1 = new Counter('m4_i1_violations');
var mI2 = new Counter('m4_i2_violations');
var mNeg = new Counter('m4_negative_probe_failures');

export var options = {
  vus: 1,
  iterations: 1,
  thresholds: {
    checks: ['rate==1.0'],
    m4_failed_requests: ['count==0'],
    m4_i1_violations: ['count==0'],
    m4_i2_violations: ['count==0'],
    m4_negative_probe_failures: ['count==0'],
  },
};

function runId() {
  var v = (__ENV.M4_RUN_ID || '').trim() || 'm4-local-run';
  return v.replace(/[^A-Za-z0-9-]/g, '-').substring(0, 64);
}

function baseUrl() {
  return ((__ENV.BASE_URL || '').trim() || 'http://localhost:5010').replace(/\/+$/, '');
}

// REFUSE TO RUN unless the frozen manifest is fully green.
export function setup() {
  var v = validateManifest(MANIFEST);
  console.log('M4-MANIFEST ' + JSON.stringify({
    runId: runId(),
    projectionReady: MANIFEST.projectionReady,
    publicationReady: MANIFEST.publicationReady,
    mongoVerified: MANIFEST.mongoVerified,
    elasticsearchVerified: MANIFEST.elasticsearchVerified,
    storefrontVerified: MANIFEST.storefrontVerified,
    isolationProbesVerified: MANIFEST.isolationProbesVerified,
    stockBudgetVerified: MANIFEST.stockBudgetVerified,
    verifiedAtUtc: MANIFEST.verifiedAtUtc || null,
    seedVersion: MANIFEST.seedVersion || null,
  }));
  if (!v.ok) fail('M4 REFUSE TO RUN: manifest validation failed: ' + v.failures.join('; '));
  return { manifestOk: true };
}

// Allowlisted read-only paths for this scenario. Anything else is refused.
var ALLOWLIST = [
  '/api/storefront/resolve-slug/',
  '/api/storefront/store-config',
  '/api/storefront/categories',
  '/api/storefront/products',
  '/api/storefront/products/by-id/',
  '/api/storefront/search',
  '/api/storefront/search/suggest',
  '/api/storefront/home/featured-products',
];

function assertAllowed(path) {
  for (var i = 0; i < ALLOWLIST.length; i++) {
    if (path.indexOf(ALLOWLIST[i]) === 0) return;
  }
  fail('M4 REFUSE: path not in read-only allowlist: ' + path);
}

export default function () {
  if (__VU !== 1) fail('M4.0 allows only VU 1 (got VU ' + __VU + ')');
  var t0 = Date.now();
  var rid = runId();
  var base = baseUrl();

  // Deterministic mapping: VU 1 -> tenant-a (manifest identity).
  var T = tenantForVu(MANIFEST, __VU);
  if (T.key !== 'a') fail('M4.0 executes VU 1 -> tenant-a only (mapped to tenant-' + T.key + ')');
  var foreign = foreignStringsFor(MANIFEST, T.key);
  var ownPrefix = T.probe.probeSku.split('-').slice(0, 2).join('-'); // e.g. LT-PROBE-A -> LT-PROBE

  var ctx = {
    vuId: String(__VU),
    userKey: null, // anonymous journey: no user identity exists
    tenantId: T.tenantId,
    storeId: T.storeId,
    slug: T.slug,
    storeSlug: T.slug,
    tenantIdHeader: T.tenantId,
    sessionId: 'sess-' + T.slug + '-vu-' + __VU + '-iter-' + __ITER,
    hostOverride: T.slug + '.localhost:5174',
  };
  var tagsFor = function (endpoint) {
    return {
      tenant: T.tenantId,
      store: T.slug,
      profile: 'm4-deterministic-read',
      endpoint: endpoint,
      vu_kind: 'm4-single',
      run_id: rid,
      iteration: String(__ITER),
    };
  };

  console.log('M4-CTX ' + JSON.stringify({
    runId: rid, vuId: ctx.vuId, iteration: __ITER,
    tenantKey: T.tenantKey, tenantId: T.tenantId, slug: T.slug,
    host: ctx.hostOverride, baseUrl: base,
    sentinel: T.sentinelProduct.slug, normal: T.normalProduct.slug,
  }));

  function violation(ctxInner, meta, detail) {
    return {
      runId: rid,
      vuId: ctxInner.vuId,
      iteration: __ITER,
      user: null, // anonymous: no user identity available
      tenant: ctxInner.tenantId,
      store: ctxInner.storeId,
      method: meta.method,
      url: meta.url,
      requestId: meta.requestId,
      correlationId: meta.correlationId,
      statusCode: meta.statusCode,
      responseHash: meta.responseBodyHash,
      foreignIds: (detail.foreignIds || []).slice(0, 20),
      expectedIdentity: detail.expected || null,
      observedIdentity: detail.observed || null,
      note: detail.note || '',
    };
  }

  // I-1 over one raw body: no foreign-tenant identifier may appear.
  function checkI1(raw, endpoint, meta) {
    var hits = [];
    var s = typeof raw === 'string' ? raw : JSON.stringify(raw);
    for (var i = 0; i < foreign.length; i++) {
      if (s.indexOf(foreign[i]) !== -1) hits.push(foreign[i]);
    }
    var pass = hits.length === 0;
    if (!pass) {
      mI1.add(1, tagsFor(endpoint));
      console.error('M4-VIOLATION ' + JSON.stringify(violation(ctx, meta, {
        foreignIds: hits, note: 'I-1 foreign tenant data in ' + endpoint,
        expected: { tenantId: T.tenantId, slug: T.slug },
      })));
    }
    check({ i1_pass: pass }, { 'i1_no_foreign_products': function (v) { return v; } });
    return pass;
  }

  function get(path, endpoint) {
    assertAllowed(path);
    var r = getWithMetadata(base + path, {
      tenantId: ctx.tenantIdHeader,
      sessionId: ctx.sessionId,
      hostOverride: ctx.hostOverride,
      tags: tagsFor(endpoint),
    });
    mHttpLatency.add(r.meta.latencyMs, tagsFor(endpoint));
    var okStatus = check(r.res, {
      'status is 2xx/3xx (never 4xx/5xx on positive reads)': function (res) {
        return res.status >= 200 && res.status < 400;
      },
    });
    if (!okStatus) {
      mFailed.add(1, tagsFor(endpoint));
      console.error('M4-VIOLATION ' + JSON.stringify(violation(ctx, r.meta, {
        note: 'unexpected status on ' + endpoint,
        expected: { statusClass: '2xx/3xx' },
        observed: { statusCode: r.meta.statusCode },
      })));
    }
    var i1 = checkI1(r.res.body || '', endpoint, r.meta);
    console.log('M4-STEP ' + JSON.stringify({
      runId: rid, endpoint: endpoint, method: 'GET', path: path,
      status: r.meta.statusCode, latencyMs: r.meta.latencyMs,
      requestId: r.meta.requestId, correlationId: r.meta.correlationId,
      bodyHash: r.meta.responseBodyHash, i1: i1 ? 'pass' : 'FAIL',
    }));
    return r;
  }

  // --- 1. resolve-slug with the manifest probe product slug (NOT tenant slug)
  var r1 = get('/api/storefront/resolve-slug/' + encodeURIComponent(T.probe.probeSlug), 'resolve-slug');
  try {
    var b1 = JSON.parse(r1.res.body || '{}');
    var i2a = b1.canonicalSlug === T.probe.probeSlug && b1.id === T.sentinelProduct.productId;
    if (!i2a) {
      mI2.add(1, tagsFor('resolve-slug'));
      console.error('M4-VIOLATION ' + JSON.stringify(violation(ctx, r1.meta, {
        note: 'I-2 resolve-slug identity mismatch',
        expected: { canonicalSlug: T.probe.probeSlug, id: T.sentinelProduct.productId },
        observed: { canonicalSlug: b1.canonicalSlug || null, id: b1.id || null, type: b1.type || null },
      })));
    }
    check({ i2a: i2a }, { 'i2_resolve_slug_identity': function (v) { return v; } });
  } catch (e) {
    mI2.add(1, tagsFor('resolve-slug'));
    fail('M4 I-2 FAILED: resolve-slug body is not JSON');
  }

  // --- 2. store-config (I-2: must bind to the manifest tenant) -----------------
  var r2 = get('/api/storefront/store-config', 'store-config');
  try {
    var b2 = JSON.parse(r2.res.body || '{}');
    var i2b = b2.tenantId === T.tenantId;
    if (!i2b) {
      mI2.add(1, tagsFor('store-config'));
      console.error('M4-VIOLATION ' + JSON.stringify(violation(ctx, r2.meta, {
        note: 'I-2 store-config tenant mismatch',
        expected: { tenantId: T.tenantId },
        observed: { tenantId: b2.tenantId || null },
      })));
    }
    check({ i2b: i2b }, { 'i2_store_config_identity': function (v) { return v; } });
  } catch (e) {
    mI2.add(1, tagsFor('store-config'));
    fail('M4 I-2 FAILED: store-config body is not JSON');
  }

  // --- 3..6. categories, product list, by-id normal + sentinel -----------------
  get('/api/storefront/categories', 'categories');
  get('/api/storefront/products', 'products');
  get('/api/storefront/products/by-id/' + T.normalProduct.productId, 'product-by-id-normal');
  var r6 = get('/api/storefront/products/by-id/' + T.sentinelProduct.productId, 'product-by-id-sentinel');
  try {
    var b6 = JSON.parse(r6.res.body || '{}');
    var sentinelOk = b6.sku === T.probe.probeSku && b6.slug === T.probe.probeSlug;
    check({ s: sentinelOk }, { 'sentinel_fingerprint_match': function (v) { return v; } });
    if (!sentinelOk) {
      mFailed.add(1, tagsFor('product-by-id-sentinel'));
      console.error('M4-VIOLATION ' + JSON.stringify(violation(ctx, r6.meta, {
        note: 'sentinel fingerprint mismatch',
        expected: { sku: T.probe.probeSku, slug: T.probe.probeSlug },
        observed: { sku: b6.sku || null, slug: b6.slug || null },
      })));
    }
  } catch (e) { mFailed.add(1, tagsFor('product-by-id-sentinel')); fail('M4 FAILED: sentinel by-id body is not JSON'); }

  // --- 7..8. search own sentinel SKU + suggest own sentinel prefix --------------
  get('/api/storefront/search?q=' + encodeURIComponent(T.probe.probeSku), 'search');
  get('/api/storefront/search/suggest?q=' + encodeURIComponent(ownPrefix), 'search-suggest');

  // --- 9. one safe homepage read (discovery-verified; 200, [] when unflagged) ----
  get('/api/storefront/home/featured-products', 'home-featured');

  // --- 10. read-only negative probe: tenant-A ctx, tenant-B sentinel by-id ------
  var negPath = '/api/storefront/products/by-id/' + tenantForVu(MANIFEST, 2).sentinelProduct.productId;
  assertAllowed(negPath);
  var rn = getWithMetadata(base + negPath, {
    tenantId: ctx.tenantIdHeader,
    sessionId: ctx.sessionId,
    hostOverride: ctx.hostOverride,
    tags: tagsFor('negative-by-id-foreign'),
  });
  mHttpLatency.add(rn.meta.latencyMs, tagsFor('negative-by-id-foreign'));
  var negPass = rn.res.status === 404;
  if (!negPass) {
    mNeg.add(1, tagsFor('negative-by-id-foreign'));
    console.error('M4-VIOLATION ' + JSON.stringify(violation(ctx, rn.meta, {
      note: 'negative probe did not return 404',
      expected: { statusCode: 404 },
      observed: { statusCode: rn.meta.statusCode },
    })));
  }
  check({ n: negPass }, { 'negative_probe_returns_404': function (v) { return v; } });
  var i1n = checkI1(rn.res.body || '', 'negative-by-id-foreign', rn.meta);
  console.log('M4-STEP ' + JSON.stringify({
    runId: rid, endpoint: 'negative-by-id-foreign', method: 'GET', path: negPath,
    status: rn.meta.statusCode, latencyMs: rn.meta.latencyMs,
    requestId: rn.meta.requestId, correlationId: rn.meta.correlationId,
    bodyHash: rn.meta.responseBodyHash, negativeProbe: negPass ? 'pass' : 'FAIL', i1: i1n ? 'pass' : 'FAIL',
  }));

  mJourney.add(Date.now() - t0, { run_id: rid, tenant: T.tenantId, store: T.slug });
  console.log('M4-DONE ' + JSON.stringify({ runId: rid, journeyMs: Date.now() - t0 }));
}

export function handleSummary(data) {
  var rid = runId();
  var text = 'M4.0 run ' + rid + ': iterations=' + (data.metrics.iterations ? data.metrics.iterations.values.count : '?')
    + ' checks_pass=' + (data.metrics.checks ? data.metrics.checks.values.passes : '?')
    + ' checks_fail=' + (data.metrics.checks ? data.metrics.checks.values.fails : '?')
    + ' m4_failed_requests=' + JSON.stringify(data.metrics.m4_failed_requests ? data.metrics.m4_failed_requests.values : null)
    + ' m4_i1_violations=' + JSON.stringify(data.metrics.m4_i1_violations ? data.metrics.m4_i1_violations.values : null)
    + ' m4_i2_violations=' + JSON.stringify(data.metrics.m4_i2_violations ? data.metrics.m4_i2_violations.values : null)
    + ' m4_negative_probe_failures=' + JSON.stringify(data.metrics.m4_negative_probe_failures ? data.metrics.m4_negative_probe_failures.values : null);
  console.log(text);
  var summary = {
    marker: 'M4.0 DETERMINISTIC SINGLE-VU READ (local validation only, not capacity)',
    runId: rid,
    manifest: {
      projectionReady: MANIFEST.projectionReady,
      publicationReady: MANIFEST.publicationReady,
      mongoVerified: MANIFEST.mongoVerified,
      elasticsearchVerified: MANIFEST.elasticsearchVerified,
      storefrontVerified: MANIFEST.storefrontVerified,
      isolationProbesVerified: MANIFEST.isolationProbesVerified,
      stockBudgetVerified: MANIFEST.stockBudgetVerified,
      verifiedAtUtc: MANIFEST.verifiedAtUtc || null,
      seedVersion: MANIFEST.seedVersion || null,
    },
    execution: { vus: 1, iterations: 1 },
    metrics: data.metrics,
  };
  var out = {};
  out['tests/load/results/m4-' + rid + '-summary.json'] = JSON.stringify(summary, null, 2);
  out.stdout = text + '\n';
  return out;
}

// M4.1 CONCURRENT MULTI-TENANT READ VALIDATION (READ-ONLY).
//
// HARD SCOPE: READ-ONLY. GET only (lib/http.js throws on any other method).
// No checkout, carts, POS sales, transactional writes, stress/soak/spike/
// breakpoint/capacity/SLO claims, DB mutation, Mongo/ES direct writes, Redis
// direct mutation, provider calls, webhooks, or TestAuth.
// No production/business code changes. No manifest changes. No reseed.
//
// OBJECTIVE: prove tenant/store isolation when three tenants run the same
// read workload CONCURRENTLY against the same backend/infrastructure.
// Mapping: VU 1 -> tenant-a, VU 2 -> tenant-b, VU 3 -> tenant-c.
//
// Each VU executes 10 complete read journeys => 3 x 10 = 30 journeys.
// Each journey = M4.0 9-step read sequence + exactly ONE cross-tenant
// negative probe (a->b, b->c, c->a), expected 404. No full matrix.
//
// Manifest (tests/load/data/manifests/latest.json) is the source of truth:
// IDs come from it via lib/manifest.js, never duplicated into this script.
// setup() validates all seven readiness flags and REFUSES TO RUN otherwise.
//
// Concurrency: per-vu-iterations executor, 3 VUs, 10 iterations each.
// All 3 VUs are active concurrently (never sequential). A small bounded
// DETERMINISTIC stagger sleep (0..0.15s, function of VU+ITER) avoids lock-step
// hammering while remaining reproducible (no large random distribution).
//
// Run exactly (twice, see M4.1 report):
//   k6 run tests/load/k6/m4-concurrent-read.js   (vus/iterations in options)
// Env: BASE_URL (default http://localhost:5010), M41_RUN_ID (default m41-local-run).

import { check, fail, sleep } from 'k6';
import { Trend, Counter } from 'k6/metrics';
import { getWithMetadata } from './lib/http.js';
import { validateManifest, tenantForVu, foreignStringsFor } from './lib/manifest.js';

// --- frozen manifest, loaded once at init (source of truth) -----------------
var MANIFEST = JSON.parse(open('../data/manifests/latest.json'));

// --- metrics (m41_* compact set; NO latency thresholds) ----------------------
var mHttpLatency = new Trend('m41_http_latency_ms');
var mJourney = new Trend('m41_journey_duration_ms');
var mFailed = new Counter('m41_failed_requests');
var mI1 = new Counter('m41_i1_violations');
var mI2 = new Counter('m41_i2_violations');
var mNeg = new Counter('m41_negative_probe_failures');

export var options = {
  scenarios: {
    concurrent_read: {
      executor: 'per-vu-iterations',
      vus: 3,
      iterations: 10,
      maxDuration: '10m',
    },
  },
  thresholds: {
    checks: ['rate==1.0'],
    m41_failed_requests: ['count==0'],
    m41_i1_violations: ['count==0'],
    m41_i2_violations: ['count==0'],
    m41_negative_probe_failures: ['count==0'],
  },
};

function runId() {
  var v = (__ENV.M41_RUN_ID || '').trim() || 'm41-local-run';
  return v.replace(/[^A-Za-z0-9-]/g, '-').substring(0, 64);
}

function baseUrl() {
  return ((__ENV.BASE_URL || '').trim() || 'http://localhost:5010').replace(/\/+$/, '');
}

// REFUSE TO RUN unless the frozen manifest is fully green.
export function setup() {
  var v = validateManifest(MANIFEST);
  console.log('M41-MANIFEST ' + JSON.stringify({
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
  if (!v.ok) fail('M4.1 REFUSE TO RUN: manifest validation failed: ' + v.failures.join('; '));
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
  fail('M4.1 REFUSE: path not in read-only allowlist: ' + path);
}

// Exactly one cross-tenant negative probe per journey (no matrix):
// VU 1 (a) -> b sentinel, VU 2 (b) -> c sentinel, VU 3 (c) -> a sentinel.
var NEG_TARGET_VU = { 1: 2, 2: 3, 3: 1 };

export default function () {
  if (__VU < 1 || __VU > 3) fail('M4.1 allows only VU 1..3 (got VU ' + __VU + ')');
  var t0 = Date.now();
  var rid = runId();
  var base = baseUrl();

  // Deterministic mapping from manifest + VU (expected identity NEVER from DTO).
  var T = tenantForVu(MANIFEST, __VU);
  var negVu = NEG_TARGET_VU[__VU];
  var N = tenantForVu(MANIFEST, negVu);
  var foreign = foreignStringsFor(MANIFEST, T.key);
  var ownPrefix = T.probe.probeSku.split('-').slice(0, 2).join('-'); // e.g. LT-PROBE

  // Foreign sentinel fingerprint for the negative-probe body leak check.
  var foreignSentinelFp = [
    N.sentinelProduct.productId,
    N.probe.probeSku,
    N.probe.probeSlug,
    N.probe.probeBarcode,
    N.probe.probePrice,
    N.probe.probeName,
  ];

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
      profile: 'm4-concurrent-read',
      endpoint: endpoint,
      vu_kind: 'm41-concurrent',
      run_id: rid,
      iteration: String(__ITER),
    };
  };

  if (__ITER === 0) {
    console.log('M41-VU-START ' + JSON.stringify({
      runId: rid, vuId: ctx.vuId, tenantKey: T.tenantKey, tenantId: T.tenantId,
      slug: T.slug, host: ctx.hostOverride, baseUrl: base, wallMs: Date.now(),
    }));
  }
  console.log('M41-CTX ' + JSON.stringify({
    runId: rid, vuId: ctx.vuId, iteration: __ITER,
    tenantKey: T.tenantKey, tenantId: T.tenantId, slug: T.slug,
    host: ctx.hostOverride, baseUrl: base,
    sentinel: T.sentinelProduct.slug, normal: T.normalProduct.slug,
    negTarget: N.tenantKey, negSentinel: N.sentinelProduct.productId,
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
    console.log('M41-STEP ' + JSON.stringify({
      runId: rid, vuId: ctx.vuId, iteration: __ITER,
      endpoint: endpoint, method: 'GET', path: path,
      status: r.meta.statusCode, latencyMs: r.meta.latencyMs,
      requestId: r.meta.requestId, correlationId: r.meta.correlationId,
      bodyHash: r.meta.responseBodyHash, i1: i1 ? 'pass' : 'FAIL',
    }));
    return r;
  }

  // --- 1. resolve-slug with own manifest probe product slug (I-2) ------------
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
    fail('M4.1 I-2 FAILED: resolve-slug body is not JSON');
  }

  // --- 2. store-config (I-2: must bind to the manifest tenant) ----------------
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
    fail('M4.1 I-2 FAILED: store-config body is not JSON');
  }

  // --- 3..6. categories, product list, by-id normal + sentinel ----------------
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
  } catch (e) { mFailed.add(1, tagsFor('product-by-id-sentinel')); fail('M4.1 FAILED: sentinel by-id body is not JSON'); }

  // --- 7..8. search own sentinel SKU + suggest own sentinel prefix -------------
  get('/api/storefront/search?q=' + encodeURIComponent(T.probe.probeSku), 'search');
  get('/api/storefront/search/suggest?q=' + encodeURIComponent(ownPrefix), 'search-suggest');

  // --- 9. one safe homepage read (discovery-verified; 200, [] when unflagged) ---
  get('/api/storefront/home/featured-products', 'home-featured');

  // --- 10. exactly ONE cross-tenant negative probe per journey (expected 404) --
  var negPath = '/api/storefront/products/by-id/' + N.sentinelProduct.productId;
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
  // Negative-probe body must carry no foreign sentinel fingerprint (leak check).
  var negBody = rn.res.body || '';
  var fpHits = [];
  for (var f = 0; f < foreignSentinelFp.length; f++) {
    if (negBody.indexOf(String(foreignSentinelFp[f])) !== -1) fpHits.push(foreignSentinelFp[f]);
  }
  var fpPass = fpHits.length === 0;
  if (!fpPass) {
    mNeg.add(1, tagsFor('negative-by-id-foreign'));
    console.error('M4-VIOLATION ' + JSON.stringify(violation(ctx, rn.meta, {
      foreignIds: fpHits, note: 'negative probe body leaks foreign sentinel fingerprint',
      expected: { statusCode: 404, foreignFingerprint: 'absent' },
      observed: { statusCode: rn.meta.statusCode },
    })));
  }
  check({ fp: fpPass }, { 'negative_probe_no_foreign_fingerprint': function (v) { return v; } });
  var i1n = checkI1(negBody, 'negative-by-id-foreign', rn.meta);
  console.log('M41-STEP ' + JSON.stringify({
    runId: rid, vuId: ctx.vuId, iteration: __ITER,
    endpoint: 'negative-by-id-foreign', method: 'GET', path: negPath,
    status: rn.meta.statusCode, latencyMs: rn.meta.latencyMs,
    requestId: rn.meta.requestId, correlationId: rn.meta.correlationId,
    bodyHash: rn.meta.responseBodyHash, negativeProbe: negPass ? 'pass' : 'FAIL',
    negFingerprint: fpPass ? 'pass' : 'FAIL', i1: i1n ? 'pass' : 'FAIL',
  }));

  mJourney.add(Date.now() - t0, { run_id: rid, tenant: T.tenantId, store: T.slug });
  console.log('M41-DONE ' + JSON.stringify({ runId: rid, vuId: ctx.vuId, iteration: __ITER, journeyMs: Date.now() - t0 }));
  if (__ITER === 9) {
    console.log('M41-VU-END ' + JSON.stringify({ runId: rid, vuId: ctx.vuId, wallMs: Date.now() }));
  }

  // Bounded deterministic stagger between iterations (0..0.15s): avoids
  // lock-step hammering, stays reproducible. No large random distribution.
  sleep(0.05 * ((__VU + __ITER) % 4));
}

export function handleSummary(data) {
  var rid = runId();
  var text = 'M4.1 run ' + rid + ': iterations=' + (data.metrics.iterations ? data.metrics.iterations.values.count : '?')
    + ' vus_max=' + (data.metrics.vus_max ? data.metrics.vus_max.values.max : '?')
    + ' checks_pass=' + (data.metrics.checks ? data.metrics.checks.values.passes : '?')
    + ' checks_fail=' + (data.metrics.checks ? data.metrics.checks.values.fails : '?')
    + ' m41_failed_requests=' + JSON.stringify(data.metrics.m41_failed_requests ? data.metrics.m41_failed_requests.values : null)
    + ' m41_i1_violations=' + JSON.stringify(data.metrics.m41_i1_violations ? data.metrics.m41_i1_violations.values : null)
    + ' m41_i2_violations=' + JSON.stringify(data.metrics.m41_i2_violations ? data.metrics.m41_i2_violations.values : null)
    + ' m41_negative_probe_failures=' + JSON.stringify(data.metrics.m41_negative_probe_failures ? data.metrics.m41_negative_probe_failures.values : null);
  console.log(text);
  var summary = {
    marker: 'M4.1 CONCURRENT MULTI-TENANT READ (local validation only, not capacity)',
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
    execution: { vus: 3, iterationsPerVu: 10, journeys: 30 },
    metrics: data.metrics,
  };
  var out = {};
  out['tests/load/results/m41-' + rid + '-summary.json'] = JSON.stringify(summary, null, 2);
  out.stdout = text + '\n';
  return out;
}

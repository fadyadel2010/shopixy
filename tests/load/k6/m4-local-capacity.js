// M4.2-A LOCAL CAPACITY PROBE (READ-ONLY).
//
// HARD SCOPE: READ-ONLY. GET only (lib/http.js throws on any other method).
// No checkout, cart mutation, POS sales, order creation, inventory
// reservation, payment, shipping, provider calls, WhatsApp/email, webhooks,
// maintenance/debug/reindex, direct Mongo/ES/Redis writes, seeding, soak,
// spike, breakpoint/destructive overload. Max 250 VUs in this phase.
// No production/business code changes. No manifest changes. No reseed.
//
// OBJECTIVE: first controlled increase in read concurrency against the local
// topology (Windows k6 + API; Ubuntu PG/Mongo/Redis/RMQ/ES). NOT production
// capacity testing. Correctness checks are NOT weakened for performance: any
// isolation/correctness failure FAILS the stage even if latency looks good.
//
// WORKLOAD: the exact M4.1 10-request journey (9 own-context reads + 1
// cross-tenant negative probe, expected 404). No new endpoints, no auth.
//
// LADDER (sequential stages, never concurrent; each stage 3 iter/VU):
//   Stage 0: 3 VUs (90 reqs) | 1: 5 (150) | 2: 10 (300) | 3: 25 (750) |
//   Stage 4: 50 (1500) | 5: 100 (3000) | 6: 250 (7500).
// Tenant mapping cycles deterministically VU 1->A, 2->B, 3->C, 4->A, ...
// (lib/manifest.js tenantForVu). Multiple VUs share a tenant past VU 3.
// Negative probe is keyed by OWN TENANT (a->b, b->c, c->a sentinels), so it
// generalizes to cycled VUs without a probe matrix.
//
// RUN: M42_VUS selects the stage VU count (1..250), M42_RUN_ID stamps tags.
//   k6 run tests/load/k6/m4-local-capacity.js   (M42_VUS=25 M42_RUN_ID=...)
// Env: BASE_URL (default http://localhost:5010).
//
// Manifest (tests/load/data/manifests/latest.json) is the sole source of
// tenant/product/probe identity. setup() validates all seven readiness flags
// and REFUSES TO RUN otherwise.

import { check, fail, sleep } from 'k6';
import { Trend, Counter, Rate } from 'k6/metrics';
import { getWithMetadata } from './lib/http.js';
import { validateManifest, tenantForVu, foreignStringsFor } from './lib/manifest.js';

// --- frozen manifest, loaded once at init (source of truth) -----------------
var MANIFEST = JSON.parse(open('../data/manifests/latest.json'));

// --- stage VU count from env (fail closed outside 1..250) --------------------
var STAGE_VUS = parseInt((__ENV.M42_VUS || '3'), 10);
if (isNaN(STAGE_VUS) || STAGE_VUS < 1 || STAGE_VUS > 250) {
  fail('M4.2-A REFUSE: M42_VUS must be 1..250 (got ' + __ENV.M42_VUS + ')');
}

// --- metrics (m42_* compact set; NO latency thresholds) ----------------------
var mHttpLatency = new Trend('m42_http_latency_ms');
var mJourney = new Trend('m42_journey_duration_ms');
var mFailed = new Counter('m42_failed_requests');
var mI1 = new Counter('m42_i1_violations');
var mI2 = new Counter('m42_i2_violations');
var mNeg = new Counter('m42_negative_probe_failures');

export var options = {
  scenarios: {
    capacity_probe: {
      executor: 'per-vu-iterations',
      vus: STAGE_VUS,
      iterations: 3,
      maxDuration: '20m',
    },
  },
  thresholds: {
    checks: ['rate==1.0'],
    m42_failed_requests: ['count==0'],
    m42_i1_violations: ['count==0'],
    m42_i2_violations: ['count==0'],
    m42_negative_probe_failures: ['count==0'],
  },
};

function runId() {
  var v = (__ENV.M42_RUN_ID || '').trim() || 'm42-local-probe';
  return v.replace(/[^A-Za-z0-9-]/g, '-').substring(0, 64);
}

function baseUrl() {
  return ((__ENV.BASE_URL || '').trim() || 'http://localhost:5010').replace(/\/+$/, '');
}

// REFUSE TO RUN unless the frozen manifest is fully green.
export function setup() {
  var v = validateManifest(MANIFEST);
  console.log('M42-MANIFEST ' + JSON.stringify({
    runId: runId(),
    stageVus: STAGE_VUS,
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
  if (!v.ok) fail('M4.2-A REFUSE TO RUN: manifest validation failed: ' + v.failures.join('; '));
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
  fail('M4.2-A REFUSE: path not in read-only allowlist: ' + path);
}

// Negative probe keyed by OWN TENANT KEY (a->b, b->c, c->a). Expressed as the
// VU number whose cycled tenant is the target, so cycled VUs (>3) keep the
// exact M4.1 single-pair semantics with no probe matrix.
var NEG_TARGET_VU_BY_KEY = { a: 2, b: 3, c: 1 };

export default function () {
  if (__VU < 1 || __VU > STAGE_VUS) fail('M4.2-A VU out of stage range (got VU ' + __VU + ', stage ' + STAGE_VUS + ')');
  var t0 = Date.now();
  var rid = runId();
  var base = baseUrl();
  var stage = String(STAGE_VUS);

  // Deterministic cycled mapping from manifest + VU (expected identity NEVER
  // from a DTO). Documented consequence: VUs > 3 share tenants.
  var T = tenantForVu(MANIFEST, __VU);
  var N = tenantForVu(MANIFEST, NEG_TARGET_VU_BY_KEY[T.key]);
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
      profile: 'm4-local-capacity',
      endpoint: endpoint,
      vu_kind: 'm42-capacity',
      run_id: rid,
      iteration: String(__ITER),
      stage: stage,
    };
  };

  if (__ITER === 0) {
    console.log('M42-VU-START ' + JSON.stringify({
      runId: rid, stage: stage, vuId: ctx.vuId, tenantKey: T.tenantKey,
      tenantId: T.tenantId, slug: T.slug, host: ctx.hostOverride,
      baseUrl: base, wallMs: Date.now(),
    }));
  }
  console.log('M42-CTX ' + JSON.stringify({
    runId: rid, stage: stage, vuId: ctx.vuId, iteration: __ITER,
    tenantKey: T.tenantKey, tenantId: T.tenantId, slug: T.slug,
    host: ctx.hostOverride, baseUrl: base,
    sentinel: T.sentinelProduct.slug, normal: T.normalProduct.slug,
    negTarget: N.tenantKey, negSentinel: N.sentinelProduct.productId,
  }));

  function violation(ctxInner, meta, detail) {
    return {
      runId: rid,
      stage: stage,
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
    console.log('M42-STEP ' + JSON.stringify({
      runId: rid, stage: stage, vuId: ctx.vuId, iteration: __ITER,
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
    fail('M4.2-A I-2 FAILED: resolve-slug body is not JSON');
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
    fail('M4.2-A I-2 FAILED: store-config body is not JSON');
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
  } catch (e) { mFailed.add(1, tagsFor('product-by-id-sentinel')); fail('M4.2-A FAILED: sentinel by-id body is not JSON'); }

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
  console.log('M42-STEP ' + JSON.stringify({
    runId: rid, stage: stage, vuId: ctx.vuId, iteration: __ITER,
    endpoint: 'negative-by-id-foreign', method: 'GET', path: negPath,
    status: rn.meta.statusCode, latencyMs: rn.meta.latencyMs,
    requestId: rn.meta.requestId, correlationId: rn.meta.correlationId,
    bodyHash: rn.meta.responseBodyHash, negativeProbe: negPass ? 'pass' : 'FAIL',
    negFingerprint: fpPass ? 'pass' : 'FAIL', i1: i1n ? 'pass' : 'FAIL',
  }));

  mJourney.add(Date.now() - t0, { run_id: rid, tenant: T.tenantId, store: T.slug, stage: stage });
  console.log('M42-DONE ' + JSON.stringify({ runId: rid, stage: stage, vuId: ctx.vuId, iteration: __ITER, journeyMs: Date.now() - t0 }));
  if (__ITER === 2) {
    console.log('M42-VU-END ' + JSON.stringify({ runId: rid, stage: stage, vuId: ctx.vuId, wallMs: Date.now() }));
  }

  // Bounded deterministic stagger between iterations (0..0.15s, M4.1 behavior):
  // avoids lock-step hammering, stays reproducible. No large random spread.
  sleep(0.05 * ((__VU + __ITER) % 4));
}

export function handleSummary(data) {
  var rid = runId();
  var text = 'M4.2-A run ' + rid + ': stageVus=' + STAGE_VUS
    + ' iterations=' + (data.metrics.iterations ? data.metrics.iterations.values.count : '?')
    + ' vus_max=' + (data.metrics.vus_max ? data.metrics.vus_max.values.max : '?')
    + ' http_reqs=' + (data.metrics.http_reqs ? data.metrics.http_reqs.values.count : '?')
    + ' checks_pass=' + (data.metrics.checks ? data.metrics.checks.values.passes : '?')
    + ' checks_fail=' + (data.metrics.checks ? data.metrics.checks.values.fails : '?')
    + ' m42_failed_requests=' + JSON.stringify(data.metrics.m42_failed_requests ? data.metrics.m42_failed_requests.values : null)
    + ' m42_i1_violations=' + JSON.stringify(data.metrics.m42_i1_violations ? data.metrics.m42_i1_violations.values : null)
    + ' m42_i2_violations=' + JSON.stringify(data.metrics.m42_i2_violations ? data.metrics.m42_i2_violations.values : null)
    + ' m42_negative_probe_failures=' + JSON.stringify(data.metrics.m42_negative_probe_failures ? data.metrics.m42_negative_probe_failures.values : null);
  console.log(text);
  var summary = {
    marker: 'M4.2-A LOCAL CAPACITY PROBE (local validation only, not capacity)',
    runId: rid,
    stageVus: STAGE_VUS,
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
    execution: { vus: STAGE_VUS, iterationsPerVu: 3, journeys: STAGE_VUS * 3 },
    metrics: data.metrics,
  };
  var out = {};
  out['tests/load/results/m42-' + rid + '-summary.json'] = JSON.stringify(summary, null, 2);
  out.stdout = text + '\n';
  return out;
}

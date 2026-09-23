// M4.3 SYNTHETIC REALISTIC STOREFRONT USER MODEL (READ-ONLY).
// CAPACITY ENVELOPE: 1000-VU STAGE ONLY. DO NOT RUN ABOVE 1000 VUs WITH THIS FILE.
//
// HARD SCOPE: READ-ONLY. GET only (lib/http.js throws on any other method).
// No login, checkout, cart writes, orders, payments, POS sales, admin, sync,
// maintenance, debug, TestAuth, direct Mongo/OpenSearch/Redis writes.
// No production/business code changes. No manifest changes. No reseed.
// M4.2-A (tests/load/k6/m4-local-capacity.js) is FROZEN and untouched by
// this phase: M4.2-A remains the backend capacity-pressure test, M4.3 is a
// separate user-behavior model.
//
// MODEL: 1 VU ~= 1 concurrently active storefront session. Each VU runs a
// session loop: pick a deterministic profile, execute its read actions with
// deterministic think times, run exactly ONE cross-tenant negative probe
// (expected 404 + no leak), repeat. Profiles (TEST MODEL, not analytics):
//   A BROWSER 40% | B SEARCHER 25% | C CATEGORY_EXPLORER 15% |
//   D QUICK_VISITOR 10% | E MIXED 10%
// See tests/load/results/M4.3_REALISTIC_USER_MODEL_DESIGN.md for the full
// specification, session math, and request-rate expectations.
//
// DETERMINISM: seeded xorshift32 PRNG (lib/realistic-user.js), seeded by
// (M43_SEED, VU, session index). No Math.random anywhere in this file.
// Tenant mapping cycles VU 1->a, 2->b, 3->c, 4->a, ... (lib/manifest.js).
// Identity is ALWAYS from the manifest, never from response DTOs.
//
// RUN (1000-VU direct stress stage; 10/25/200/300/500-VU stages validated, intermediate stages skipped by design):
//   k6 run tests/load/k6/m4-realistic-storefront.js
// Env: BASE_URL (default http://localhost:5010),
//      M43_VUS (default 10; HARD STOP guard: only 1..1000 accepted),
//      M43_RUN_ID (default m43-realistic-10vu),
//      M43_SEED (default 43711; stable constant).
//
// HARD STOP: 1000-VU direct stress stage only. This script REFUSES
// M43_VUS > 1000 so 1001+, stress above 1000, soak, write-heavy, M4.2-B
// cannot be launched with it in this task.
// DRAIN: 5m measurement + 180s graceful drain. The 180s grace is a harness-only
// change to prevent force-stop censoring at the saturated tail; the
// measurement window remains exactly 5 minutes.

import { check, fail, sleep } from 'k6';
import { Trend, Counter } from 'k6/metrics';
import { getWithMetadata } from './lib/http.js';
import { validateManifest, tenantForVu, foreignStringsFor } from './lib/manifest.js';
import {
  M43_MODEL_NAME,
  M43_SEED_DEFAULT,
  M43_PROFILE_DEFS,
  M43_THINK_RANGES_S,
  mixSeed,
  newRng,
  pickProfile,
  thinkRangeFor,
  staggerForVu,
  normalProductsOf,
} from './lib/realistic-user.js';

// --- frozen manifest, loaded once at init (source of truth) -----------------
var MANIFEST = JSON.parse(open('../data/manifests/latest.json'));

// --- stage VU guard: HARD STOP at 1000 (no >1000 in this task) --
var M43_VUS = parseInt((__ENV.M43_VUS || '10'), 10);
if (isNaN(M43_VUS) || M43_VUS < 1 || M43_VUS > 1000) {
  fail('M4.3 REFUSE: M43_VUS must be 1..1000 in this task (HARD STOP: no >1000; got ' + __ENV.M43_VUS + ')');
}

var M43_SEED = parseInt((__ENV.M43_SEED || String(M43_SEED_DEFAULT)), 10);
if (isNaN(M43_SEED)) fail('M4.3 REFUSE: M43_SEED must be an integer (got ' + __ENV.M43_SEED + ')');

// --- metrics (m43_*; NO latency thresholds, NO capacity claims) --------------
var mHttpLatency = new Trend('m43_http_latency_ms');
var mSessionDur = new Trend('m43_session_duration_ms');
var mThink = new Trend('m43_think_time_s');
var mReqPerSession = new Trend('m43_requests_per_session');
var mFailed = new Counter('m43_failed_requests');
var mI1 = new Counter('m43_i1_violations');
var mI2 = new Counter('m43_i2_violations');
var mNeg = new Counter('m43_negative_probe_failures');
var mSessions = new Counter('m43_sessions_completed');
var mActions = new Counter('m43_action_requests');

export var options = {
  scenarios: {
    realistic_users: {
      executor: 'constant-vus',
      vus: M43_VUS,
      duration: '5m',
      gracefulStop: '180s',
    },
  },
  thresholds: {
    checks: ['rate==1.0'],
    m43_failed_requests: ['count==0'],
    m43_i1_violations: ['count==0'],
    m43_i2_violations: ['count==0'],
    m43_negative_probe_failures: ['count==0'],
  },
};

function runId() {
  var v = (__ENV.M43_RUN_ID || '').trim() || 'm43-realistic-10vu';
  return v.replace(/[^A-Za-z0-9-]/g, '-').substring(0, 64);
}

function baseUrl() {
  return ((__ENV.BASE_URL || '').trim() || 'http://localhost:5010').replace(/\/+$/, '');
}

// REFUSE TO RUN unless the frozen manifest is fully green.
export function setup() {
  var v = validateManifest(MANIFEST);
  console.log('M43-MANIFEST ' + JSON.stringify({
    runId: runId(),
    model: M43_MODEL_NAME,
    vus: M43_VUS,
    seed: M43_SEED,
    duration: '5m',
    profiles: M43_PROFILE_DEFS,
    thinkRangesS: M43_THINK_RANGES_S,
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
  if (!v.ok) fail('M4.3 REFUSE TO RUN: manifest validation failed: ' + v.failures.join('; '));
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
  fail('M4.3 REFUSE: path not in read-only allowlist: ' + path);
}

// Negative probe keyed by OWN TENANT KEY (a->b, b->c, c->a). Expressed as the
// VU number whose cycled tenant is the target, so cycled VUs (>3) keep the
// exact M4.1 single-pair semantics with no probe matrix.
var NEG_TARGET_VU_BY_KEY = { a: 2, b: 3, c: 1 };

// Per-VU session state (k6 isolates module-level vars per VU runtime).
var sessionIdx = 0;
var vuStarted = false;

export default function () {
  if (__VU < 1 || __VU > M43_VUS) fail('M4.3 VU out of pilot range (got VU ' + __VU + ', pilot ' + M43_VUS + ')');

  if (!vuStarted) {
    vuStarted = true;
    var stagger = staggerForVu(M43_SEED, __VU);
    console.log('M43-VU-START ' + JSON.stringify({
      runId: runId(), vu: __VU, seed: M43_SEED, staggerS: stagger, wallMs: Date.now(),
    }));
    if (stagger > 0) sleep(stagger);
  }

  sessionIdx += 1;
  var t0 = Date.now();
  var rid = runId();
  var base = baseUrl();

  // Deterministic session RNG: same (seed, VU, session) => same decisions.
  var rng = newRng(mixSeed(M43_SEED, __VU, sessionIdx));
  var profile = pickProfile(rng.next01());

  // Deterministic cycled mapping from manifest + VU (expected identity NEVER
  // from a DTO). VUs > 3 share tenants.
  var T = tenantForVu(MANIFEST, __VU);
  var N = tenantForVu(MANIFEST, NEG_TARGET_VU_BY_KEY[T.key]);
  var foreign = foreignStringsFor(MANIFEST, T.key);
  var ownPrefix = T.probe.probeSku.split('-').slice(0, 2).join('-'); // e.g. LT-PROBE
  var normals = normalProductsOf(T.tenantRecord);

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
    userKey: null, // anonymous session: no user identity exists
    tenantId: T.tenantId,
    storeId: T.storeId,
    slug: T.slug,
    storeSlug: T.slug,
    tenantIdHeader: T.tenantId,
    sessionId: 'sess-m43-' + T.slug + '-vu-' + __VU + '-s' + sessionIdx,
    hostOverride: T.slug + '.localhost:5174',
  };

  var reqCount = 0;
  var thinkTotalS = 0;
  var thinkCount = 0;

  var tagsFor = function (action) {
    return {
      tenant: T.tenantId,
      store: T.slug,
      profile: profile,
      action: action,
      endpoint: action,
      vu: String(__VU),
      session: String(sessionIdx),
      run_id: rid,
      vu_kind: 'm43-realistic',
    };
  };

  console.log('M43-SESSION-START ' + JSON.stringify({
    runId: rid, vu: __VU, session: sessionIdx, profile: profile,
    tenantKey: T.tenantKey, tenantId: T.tenantId, slug: T.slug,
    host: ctx.hostOverride, baseUrl: base,
  }));

  function violation(ctxInner, meta, detail) {
    return {
      runId: rid,
      vu: String(__VU),
      session: sessionIdx,
      profile: profile,
      iteration: sessionIdx,
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
  function checkI1(raw, action, meta) {
    var hits = [];
    var s = typeof raw === 'string' ? raw : JSON.stringify(raw);
    for (var i = 0; i < foreign.length; i++) {
      if (s.indexOf(foreign[i]) !== -1) hits.push(foreign[i]);
    }
    var pass = hits.length === 0;
    if (!pass) {
      mI1.add(1, tagsFor(action));
      console.error('M4-VIOLATION ' + JSON.stringify(violation(ctx, meta, {
        foreignIds: hits, note: 'I-1 foreign tenant data in ' + action,
        expected: { tenantId: T.tenantId, slug: T.slug },
      })));
    }
    check({ i1_pass: pass }, { 'i1_no_foreign_products': function (v) { return v; } });
    return pass;
  }

  function get(path, action) {
    assertAllowed(path);
    var r = getWithMetadata(base + path, {
      tenantId: ctx.tenantIdHeader,
      sessionId: ctx.sessionId,
      hostOverride: ctx.hostOverride,
      tags: tagsFor(action),
    });
    reqCount += 1;
    mHttpLatency.add(r.meta.latencyMs, tagsFor(action));
    mActions.add(1, tagsFor(action));
    var okStatus = check(r.res, {
      'status is 2xx/3xx (never 4xx/5xx on positive reads)': function (res) {
        return res.status >= 200 && res.status < 400;
      },
    });
    if (!okStatus) {
      mFailed.add(1, tagsFor(action));
      console.error('M4-VIOLATION ' + JSON.stringify(violation(ctx, r.meta, {
        note: 'unexpected status on ' + action,
        expected: { statusClass: '2xx/3xx' },
        observed: { statusCode: r.meta.statusCode },
      })));
    }
    var i1 = checkI1(r.res.body || '', action, r.meta);
    console.log('M43-STEP ' + JSON.stringify({
      runId: rid, vu: __VU, session: sessionIdx, profile: profile,
      action: action, method: 'GET', path: path,
      status: r.meta.statusCode, latencyMs: r.meta.latencyMs,
      requestId: r.meta.requestId, correlationId: r.meta.correlationId,
      bodyHash: r.meta.responseBodyHash, i1: i1 ? 'pass' : 'FAIL',
    }));
    return r;
  }

  // Deterministic think pause between actions (seeded PRNG, bounded range).
  function think(kind) {
    var range = thinkRangeFor(kind);
    var d = rng.nextRange(range.min, range.max);
    thinkTotalS += d;
    thinkCount += 1;
    mThink.add(d, tagsFor('think-' + kind));
    console.log('M43-THINK ' + JSON.stringify({
      runId: rid, vu: __VU, session: sessionIdx, profile: profile,
      kind: kind, thinkS: Math.round(d * 1000) / 1000,
    }));
    sleep(d);
  }

  function resolveContext() {
    var r = get('/api/storefront/resolve-slug/' + encodeURIComponent(T.probe.probeSlug), 'resolve-slug');
    try {
      var b = JSON.parse(r.res.body || '{}');
      var ok = b.canonicalSlug === T.probe.probeSlug && b.id === T.sentinelProduct.productId;
      if (!ok) {
        mI2.add(1, tagsFor('resolve-slug'));
        console.error('M4-VIOLATION ' + JSON.stringify(violation(ctx, r.meta, {
          note: 'I-2 resolve-slug identity mismatch',
          expected: { canonicalSlug: T.probe.probeSlug, id: T.sentinelProduct.productId },
          observed: { canonicalSlug: b.canonicalSlug || null, id: b.id || null, type: b.type || null },
        })));
      }
      check({ i2a: ok }, { 'i2_resolve_slug_identity': function (v) { return v; } });
    } catch (e) {
      mI2.add(1, tagsFor('resolve-slug'));
      fail('M4.3 I-2 FAILED: resolve-slug body is not JSON');
    }
  }

  function storeConfig() {
    var r = get('/api/storefront/store-config', 'store-config');
    try {
      var b = JSON.parse(r.res.body || '{}');
      var ok = b.tenantId === T.tenantId;
      if (!ok) {
        mI2.add(1, tagsFor('store-config'));
        console.error('M4-VIOLATION ' + JSON.stringify(violation(ctx, r.meta, {
          note: 'I-2 store-config tenant mismatch',
          expected: { tenantId: T.tenantId },
          observed: { tenantId: b.tenantId || null },
        })));
      }
      check({ i2b: ok }, { 'i2_store_config_identity': function (v) { return v; } });
    } catch (e) {
      mI2.add(1, tagsFor('store-config'));
      fail('M4.3 I-2 FAILED: store-config body is not JSON');
    }
  }

  function categories() {
    get('/api/storefront/categories', 'categories');
  }

  function products() {
    get('/api/storefront/products', 'products');
  }

  function productDetailNormal(p) {
    get('/api/storefront/products/by-id/' + p.productId, 'product-detail');
  }

  function productDetailSentinel() {
    var r = get('/api/storefront/products/by-id/' + T.sentinelProduct.productId, 'product-detail-sentinel');
    try {
      var b = JSON.parse(r.res.body || '{}');
      var ok = b.sku === T.probe.probeSku && b.slug === T.probe.probeSlug;
      check({ s: ok }, { 'sentinel_fingerprint_match': function (v) { return v; } });
      if (!ok) {
        mFailed.add(1, tagsFor('product-detail-sentinel'));
        console.error('M4-VIOLATION ' + JSON.stringify(violation(ctx, r.meta, {
          note: 'sentinel fingerprint mismatch',
          expected: { sku: T.probe.probeSku, slug: T.probe.probeSlug },
          observed: { sku: b.sku || null, slug: b.slug || null },
        })));
      }
    } catch (e) { mFailed.add(1, tagsFor('product-detail-sentinel')); fail('M4.3 FAILED: sentinel by-id body is not JSON'); }
  }

  function searchOwn() {
    // Own-tenant term only (sentinel SKU or a deterministic normal SKU).
    var term = T.probe.probeSku;
    if (normals.length > 0 && rng.next01() < 0.5) {
      term = normals[rng.nextInt(normals.length)].sku;
    }
    get('/api/storefront/search?q=' + encodeURIComponent(term), 'search');
  }

  function suggestOwn() {
    get('/api/storefront/search/suggest?q=' + encodeURIComponent(ownPrefix), 'search-suggest');
  }

  function featured() {
    get('/api/storefront/home/featured-products', 'home-featured');
  }

  function pickTwoNormals() {
    if (normals.length < 2) fail('M4.3 manifest tenant has < 2 normal products');
    var first = rng.nextInt(normals.length);
    var second = (first + 1 + rng.nextInt(normals.length - 1)) % normals.length;
    return [normals[first], normals[second]];
  }

  // Exactly ONE cross-tenant negative probe per complete session cycle.
  function negativeProbe() {
    var negPath = '/api/storefront/products/by-id/' + N.sentinelProduct.productId;
    assertAllowed(negPath);
    var rn = getWithMetadata(base + negPath, {
      tenantId: ctx.tenantIdHeader,
      sessionId: ctx.sessionId,
      hostOverride: ctx.hostOverride,
      tags: tagsFor('negative-by-id-foreign'),
    });
    reqCount += 1;
    mHttpLatency.add(rn.meta.latencyMs, tagsFor('negative-by-id-foreign'));
    mActions.add(1, tagsFor('negative-by-id-foreign'));
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
    console.log('M43-STEP ' + JSON.stringify({
      runId: rid, vu: __VU, session: sessionIdx, profile: profile,
      action: 'negative-by-id-foreign', method: 'GET', path: negPath,
      status: rn.meta.statusCode, latencyMs: rn.meta.latencyMs,
      requestId: rn.meta.requestId, correlationId: rn.meta.correlationId,
      bodyHash: rn.meta.responseBodyHash, negativeProbe: negPass ? 'pass' : 'FAIL',
      negFingerprint: fpPass ? 'pass' : 'FAIL', i1: i1n ? 'pass' : 'FAIL',
    }));
  }

  // --- session entry: store context (both actions carry I-2) -----------------
  resolveContext();
  think('page');
  storeConfig();
  think('page');

  // --- profile body -----------------------------------------------------------
  if (profile === 'A') {
    // BROWSER: categories -> products -> detail -> think -> detail -> maybe more.
    categories();
    think('category');
    products();
    think('list');
    var pairA = pickTwoNormals();
    productDetailNormal(pairA[0]);
    think('detail');
    if (rng.next01() < 0.5) productDetailSentinel();
    else productDetailNormal(pairA[1]);
    think('detail');
    if (rng.next01() < 0.5) {
      products();
      think('list');
    }
  } else if (profile === 'B') {
    // SEARCHER: products|categories -> search -> detail -> search|detail.
    if (rng.next01() < 0.5) {
      products();
      think('list');
    } else {
      categories();
      think('category');
    }
    searchOwn();
    think('search');
    if (rng.next01() < 0.5) productDetailSentinel();
    else productDetailNormal(pickTwoNormals()[0]);
    think('detail');
    if (rng.next01() < 0.5) {
      searchOwn();
      think('search');
    } else {
      productDetailNormal(pickTwoNormals()[1]);
      think('detail');
    }
  } else if (profile === 'C') {
    // CATEGORY EXPLORER: categories -> products -> detail -> more browsing.
    categories();
    think('category');
    products();
    think('list');
    productDetailNormal(pickTwoNormals()[0]);
    think('detail');
    categories();
    think('category');
  } else if (profile === 'D') {
    // QUICK VISITOR: products -> one product detail -> leave.
    products();
    think('short');
    productDetailNormal(pickTwoNormals()[0]);
    think('short');
  } else {
    // E MIXED: products + search + suggest + detail + featured.
    products();
    think('list');
    searchOwn();
    think('search');
    suggestOwn();
    think('search');
    productDetailNormal(pickTwoNormals()[0]);
    think('detail');
    featured();
  }

  // --- one negative probe per complete session cycle --------------------------
  negativeProbe();

  var sessionMs = Date.now() - t0;
  mSessions.add(1, tagsFor('session'));
  mSessionDur.add(sessionMs, tagsFor('session'));
  mReqPerSession.add(reqCount, tagsFor('session'));
  console.log('M43-SESSION-END ' + JSON.stringify({
    runId: rid, vu: __VU, session: sessionIdx, profile: profile,
    tenantKey: T.tenantKey, requests: reqCount,
    thinks: thinkCount, thinkTotalS: Math.round(thinkTotalS * 1000) / 1000,
    sessionMs: sessionMs,
  }));
}

export function handleSummary(data) {
  var rid = runId();
  var text = 'M4.3 run ' + rid + ': model=' + M43_MODEL_NAME
    + ' vus=' + M43_VUS + ' seed=' + M43_SEED
    + ' iterations(sessions started)=' + (data.metrics.iterations ? data.metrics.iterations.values.count : '?')
    + ' vus_max=' + (data.metrics.vus_max ? data.metrics.vus_max.values.max : '?')
    + ' http_reqs=' + (data.metrics.http_reqs ? data.metrics.http_reqs.values.count : '?')
    + ' checks_pass=' + (data.metrics.checks ? data.metrics.checks.values.passes : '?')
    + ' checks_fail=' + (data.metrics.checks ? data.metrics.checks.values.fails : '?')
    + ' m43_sessions_completed=' + JSON.stringify(data.metrics.m43_sessions_completed ? data.metrics.m43_sessions_completed.values : null)
    + ' m43_failed_requests=' + JSON.stringify(data.metrics.m43_failed_requests ? data.metrics.m43_failed_requests.values : null)
    + ' m43_i1_violations=' + JSON.stringify(data.metrics.m43_i1_violations ? data.metrics.m43_i1_violations.values : null)
    + ' m43_i2_violations=' + JSON.stringify(data.metrics.m43_i2_violations ? data.metrics.m43_i2_violations.values : null)
    + ' m43_negative_probe_failures=' + JSON.stringify(data.metrics.m43_negative_probe_failures ? data.metrics.m43_negative_probe_failures.values : null);
  console.log(text);
  var summary = {
    marker: 'M4.3 SYNTHETIC REALISTIC STOREFRONT USER MODEL PILOT (model validation only, not capacity)',
    model: M43_MODEL_NAME,
    modelNote: 'TEST MODEL weights/think-times; NOT observed production analytics.',
    runId: rid,
    seed: M43_SEED,
    profiles: M43_PROFILE_DEFS,
    thinkRangesS: M43_THINK_RANGES_S,
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
    execution: { vus: M43_VUS, duration: '5m', executor: 'constant-vus' },
    metrics: data.metrics,
  };
  var out = {};
  // Artifact isolation: summary path is runId-aware (M43_RUN_ID, sanitized by
  // runId()). Distinct run IDs can never collide; the fallback default is
  // explicit ('m43-realistic-10vu') and logged/stamped, never silent.
  out['tests/load/results/' + rid + '-summary.json'] = JSON.stringify(summary, null, 2);
  out.stdout = text + '\n';
  return out;
}

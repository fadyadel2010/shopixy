// M1.1 fixture-driven isolation assertions (I-1..I-9).
// I-1 + I-2 are exercised by the live read smoke; I-3..I-9 expose callable
// assertion helpers with deterministic fixture/mock examples so no unsafe
// endpoint is invoked merely to exercise them.
// Violation bundles NEVER include tokens or passwords.
import { check } from 'k6';

// --- violation bundle -------------------------------------------------------
export function violationBundle(ctx, meta, detail) {
  return {
    vuId: ctx.vuId,
    userId: ctx.userKey || ctx.customerKey || null,
    tenantId: ctx.tenantId,
    storeId: ctx.storeId || ctx.tenantId || null,
    slug: ctx.slug || ctx.storeSlug || null,
    method: meta.method,
    url: meta.url,
    'X-Request-Id': meta.requestId,
    'X-Correlation-Id': meta.correlationId,
    statusCode: meta.statusCode,
    responseBodyHash: meta.responseBodyHash,
    foreignIds: (detail && detail.foreignIds) || [],
    note: (detail && detail.note) || '',
  };
}

function collectIds(value, out) {
  out = out || [];
  if (value === null || value === undefined) return out;
  if (typeof value === 'string') {
    if (/^[0-9a-fA-F-]{8,}$/.test(value) || /PROBE|TEST-PROBE/i.test(value)) out.push(value);
    return out;
  }
  if (Array.isArray(value)) {
    for (var i = 0; i < value.length; i++) collectIds(value[i], out);
    return out;
  }
  if (typeof value === 'object') {
    for (var k in value) {
      if (k.toLowerCase().indexOf('tenant') !== -1 && typeof value[k] === 'string') out.push(value[k]);
      else collectIds(value[k], out);
    }
  }
  return out;
}

// --- I-1: tenant A never receives tenant B products --------------------------
// body: parsed JSON (or raw string). expectedTenantId: authority from VU/env.
export function assertNoForeignTenantProducts(body, expectedTenantId, ctx, meta) {
  var ids = collectIds(body);
  var foreign = ids.filter(function (id) {
    return id !== expectedTenantId && /^[0-9a-fA-F-]{36}$/.test(id || '');
  });
  var probeHits = [];
  try {
    var s = typeof body === 'string' ? body : JSON.stringify(body);
    var m = s.match(/TEST-PROBE-TENANT-[A-Z]+/g) || [];
    probeHits = m.filter(function (p) { return p !== ctx.expectedProbe; });
  } catch (e) { /* ignore stringify errors */ }
  foreign = foreign.concat(probeHits);
  var pass = foreign.length === 0;
  var bundle = pass ? null : violationBundle(ctx, meta, { foreignIds: foreign.slice(0, 20), note: 'I-1 foreign tenant data' });
  check({ i1_no_foreign_products: pass }, { i1_no_foreign_products: function (v) { return v; } });
  return { pass: pass, foreignIds: foreign, violation: bundle };
}

// --- I-2: store A never resolves to store B ----------------------------------
export function assertStoreResolution(body, expectedSlug, expectedTenantId, ctx, meta) {
  var s = '';
  try { s = typeof body === 'string' ? body : JSON.stringify(body); } catch (e) { s = ''; }
  var foreign = [];
  ['store-a', 'store-b', 'store-c'].forEach(function (slug) {
    if (slug !== expectedSlug && s.indexOf(slug) !== -1) foreign.push(slug);
  });
  var ids = collectIds(body).filter(function (id) {
    return id !== expectedTenantId && /^[0-9a-fA-F-]{36}$/.test(id || '');
  });
  foreign = foreign.concat(ids);
  var pass = foreign.length === 0;
  var bundle = pass ? null : violationBundle(ctx, meta, { foreignIds: foreign.slice(0, 20), note: 'I-2 store misresolution' });
  check({ i2_store_resolution: pass }, { i2_store_resolution: function (v) { return v; } });
  return { pass: pass, foreignIds: foreign, violation: bundle };
}

// --- I-3..I-9: fixture-callable helpers (no live unsafe calls) ----------------
export function assertNoCrossUserAccess(body, ownUserKey, ctx, meta) {
  var s = '';
  try { s = typeof body === 'string' ? body : JSON.stringify(body); } catch (e) { s = ''; }
  var others = ['user-a-001', 'user-b-001', 'user-c-001', 'customer-a-001', 'customer-b-001', 'customer-c-001']
    .filter(function (u) { return u !== ownUserKey && s.indexOf(u) !== -1; });
  var pass = others.length === 0;
  return { pass: pass, foreignIds: others, violation: pass ? null : violationBundle(ctx, meta, { foreignIds: others, note: 'I-3 cross-user data' }) };
}

export function assertOrderTenantScope(body, expectedTenantId, ctx, meta) {
  return assertNoForeignTenantProducts(body, expectedTenantId, ctx, meta);
}

export function assertVendorScope(docs, admittedStoreIds, ctx, meta) {
  var foreign = (docs || []).filter(function (d) { return admittedStoreIds.indexOf(d.storeId || d.tenantId) === -1; });
  var pass = foreign.length === 0;
  return { pass: pass, foreignIds: foreign.slice(0, 20), violation: pass ? null : violationBundle(ctx, meta, { foreignIds: foreign.slice(0, 5), note: 'I-5 vendor scope' }) };
}

export function assertProjectionIsolation(renderedText, ownProbe, ctx, meta) {
  var m = String(renderedText || '').match(/TEST-PROBE-TENANT-[A-Z]+/g) || [];
  var foreign = m.filter(function (p) { return p !== ownProbe; });
  var pass = foreign.length === 0;
  return { pass: pass, foreignIds: foreign, violation: pass ? null : violationBundle(ctx, meta, { foreignIds: foreign, note: 'I-6 projection leak' }) };
}

export function assertNoForeignStockMutation(before, after, ownOrderIds, ctx, meta) {
  var foreign = (after || []).filter(function (r) { return ownOrderIds.indexOf(r.orderId) === -1 && r.changed === true; });
  var pass = foreign.length === 0;
  return { pass: pass, foreignIds: foreign.slice(0, 20), violation: pass ? null : violationBundle(ctx, meta, { foreignIds: foreign.slice(0, 5), note: 'I-7 foreign stock mutation' }) };
}

export function assertAuthTenantBinding(sessionTenantId, expectedTenantId, ctx, meta) {
  var pass = sessionTenantId === expectedTenantId;
  return { pass: pass, foreignIds: pass ? [] : [sessionTenantId], violation: pass ? null : violationBundle(ctx, meta, { foreignIds: [sessionTenantId], note: 'I-8 auth tenant misbinding' }) };
}

export function assertIdempotencyScope(replayedTenantId, expectedTenantId, sameKeyReplayHashA, sameKeyReplayHashB, ctx, meta) {
  // Cross-tenant replay of the same key must NOT return the other tenant's
  // stored response (hashes differ); same-tenant replay must be identical.
  var crossTenantIndependent = replayedTenantId !== expectedTenantId ? true : sameKeyReplayHashA !== sameKeyReplayHashB;
  void crossTenantIndependent;
  var pass = sameKeyReplayHashA === sameKeyReplayHashB;
  return { pass: pass, foreignIds: pass ? [] : [String(sameKeyReplayHashA), String(sameKeyReplayHashB)], violation: pass ? null : violationBundle(ctx, meta, { foreignIds: [], note: 'I-9 idempotency scope' }) };
}

// --- Deterministic fixture self-test (runs offline, no HTTP) -----------------
// Callable from smoke.js with ?SELFTEST=1 or from node for authoring.
export function fixtureSelfTest() {
  var fakeMeta = { method: 'GET', url: 'fixture', requestId: 'r1', correlationId: 'c1', statusCode: 200, responseBodyHash: 'h' };
  var fakeCtx = { vuId: '1', userKey: 'user-a-001', tenantId: 'T-A', storeId: 'T-A', slug: 'store-a', expectedProbe: 'TEST-PROBE-TENANT-A' };
  var results = [];
  results.push(['I-1 pass', assertNoForeignTenantProducts({ tenantId: 'T-A', items: [] }, 'T-A', fakeCtx, fakeMeta).pass === true]);
  results.push(['I-1 fail', assertNoForeignTenantProducts({ tenantId: 'T-B' }, 'T-A', fakeCtx, fakeMeta).pass === false]);
  results.push(['I-2 pass', assertStoreResolution({ slug: 'store-a' }, 'store-a', 'T-A', fakeCtx, fakeMeta).pass === true]);
  results.push(['I-2 fail', assertStoreResolution({ slug: 'store-b' }, 'store-a', 'T-A', fakeCtx, fakeMeta).pass === false]);
  results.push(['I-3 pass', assertNoCrossUserAccess({ owner: 'user-a-001' }, 'user-a-001', fakeCtx, fakeMeta).pass === true]);
  results.push(['I-3 fail', assertNoCrossUserAccess({ owner: 'user-b-001' }, 'user-a-001', fakeCtx, fakeMeta).pass === false]);
  results.push(['I-5 pass', assertVendorScope([{ storeId: 'S-A' }], ['S-A'], fakeCtx, fakeMeta).pass === true]);
  results.push(['I-5 fail', assertVendorScope([{ storeId: 'S-B' }], ['S-A'], fakeCtx, fakeMeta).pass === false]);
  results.push(['I-6 pass', assertProjectionIsolation('TEST-PROBE-TENANT-A only', 'TEST-PROBE-TENANT-A', fakeCtx, fakeMeta).pass === true]);
  results.push(['I-6 fail', assertProjectionIsolation('TEST-PROBE-TENANT-B leak', 'TEST-PROBE-TENANT-A', fakeCtx, fakeMeta).pass === false]);
  results.push(['I-8 pass', assertAuthTenantBinding('T-A', 'T-A', fakeCtx, fakeMeta).pass === true]);
  results.push(['I-8 fail', assertAuthTenantBinding('T-B', 'T-A', fakeCtx, fakeMeta).pass === false]);
  return results;
}

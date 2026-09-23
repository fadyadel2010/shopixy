// M4.3 SYNTHETIC REALISTIC STOREFRONT USER MODEL — deterministic helpers.
//
// TEST MODEL ONLY. These weights/think-times are NOT observed production
// analytics. They are a documented synthetic model until real Shopixy
// analytics are available.
//
// Determinism contract:
//   - NO Math.random() anywhere in this file (or in m4-realistic-storefront.js).
//   - Every decision derives from mixSeed(seed, vu, sessionIdx) feeding a
//     xorshift32 PRNG. Same (seed, VU, session) => same profile, same product
//     picks, same think times, every run.
//   - Pure ES5 (var/function only) so the file runs identically under k6/goja
//     and under node (used by the offline unit harness).
//
// Profile weights (TEST MODEL, sums to 100):
//   A BROWSER            40%  resolve/store -> categories -> products ->
//                            product detail -> think -> product detail ->
//                            think -> maybe return to products
//   B SEARCHER           25%  resolve/store -> products|Categories -> search ->
//                            product detail -> think -> search|product detail
//   C CATEGORY_EXPLORER  15%  resolve/store -> categories -> products ->
//                            product detail -> think -> more browsing
//   D QUICK_VISITOR      10%  resolve/store -> products -> one product detail
//                            -> leave (short pauses only)
//   E MIXED              10%  products + search + suggest + product detail +
//                            featured
//
// Think-time ranges in seconds (bounded, deterministic per draw):
//   page      1-3   (context/resolve/store-config transitions)
//   list      2-5   (product list inspection)
//   detail    5-12  (product detail reading)
//   search    2-5   (search typing/reading, suggest reading)
//   category  2-6   (category browsing)
//   short     1-4   (quick-visitor pauses)

export var M43_MODEL_NAME = 'SYNTHETIC REALISTIC STOREFRONT USER MODEL';
export var M43_SEED_DEFAULT = 43711;

export var M43_PROFILE_DEFS = [
  { name: 'A', label: 'BROWSER', weight: 40 },
  { name: 'B', label: 'SEARCHER', weight: 25 },
  { name: 'C', label: 'CATEGORY_EXPLORER', weight: 15 },
  { name: 'D', label: 'QUICK_VISITOR', weight: 10 },
  { name: 'E', label: 'MIXED', weight: 10 },
];

export var M43_THINK_RANGES_S = {
  page: { min: 1, max: 3 },
  list: { min: 2, max: 5 },
  detail: { min: 5, max: 12 },
  search: { min: 2, max: 5 },
  category: { min: 2, max: 6 },
  short: { min: 1, max: 4 },
};

// FNV-1a 32-bit over a string. Unsigned 32-bit result.
export function fnv1a32(s) {
  var h = 2166136261;
  for (var i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = (h * 16777619) >>> 0;
  }
  return h >>> 0;
}

// Combine the stable run seed with VU + session index into one uint32 state.
// String-keyed FNV keeps the derivation identical across JS engines.
export function mixSeed(seed, vu, sessionIdx) {
  return fnv1a32('m43|' + String(seed) + '|' + String(vu) + '|' + String(sessionIdx));
}

// xorshift32 step. Input/output are unsigned 32-bit ints; never returns 0
// state (0 maps to a fixed nonzero fallback so the stream cannot stick).
export function xorshift32Step(state) {
  var s = state >>> 0;
  if (s === 0) s = 0x9e3779b9;
  s ^= (s << 13) >>> 0;
  s = s >>> 0;
  s ^= s >>> 17;
  s ^= (s << 5) >>> 0;
  s = s >>> 0;
  if (s === 0) s = 0x9e3779b9;
  return s >>> 0;
}

// Seeded PRNG handle (closure). next01() in [0,1); nextRange(min,max) in
// [min,max); nextInt(n) in [0,n).
export function newRng(stateUint32) {
  var st = (stateUint32 >>> 0) || 0x9e3779b9;
  function nextU32() {
    st = xorshift32Step(st);
    return st;
  }
  return {
    nextU32: nextU32,
    next01: function () {
      return nextU32() / 4294967296;
    },
    nextRange: function (min, max) {
      return min + (nextU32() / 4294967296) * (max - min);
    },
    nextInt: function (n) {
      if (!(n > 0)) throw new Error('M4.3 RNG: nextInt requires n>0');
      return Math.floor((nextU32() / 4294967296) * n);
    },
  };
}

// Map a uniform draw u in [0,1) onto a profile name via the weight table.
export function pickProfile(u) {
  var total = 0;
  var i;
  for (i = 0; i < M43_PROFILE_DEFS.length; i++) total += M43_PROFILE_DEFS[i].weight;
  var x = u * total;
  var acc = 0;
  for (i = 0; i < M43_PROFILE_DEFS.length; i++) {
    acc += M43_PROFILE_DEFS[i].weight;
    if (x < acc) return M43_PROFILE_DEFS[i].name;
  }
  return M43_PROFILE_DEFS[M43_PROFILE_DEFS.length - 1].name;
}

// Think-time range lookup. Throws on unknown kind (fail closed).
export function thinkRangeFor(kind) {
  var r = M43_THINK_RANGES_S[kind];
  if (!r) throw new Error('M4.3 unknown think kind: ' + String(kind));
  return r;
}

// Deterministic per-VU start stagger in seconds, spread over [0, 4.9].
// Desynchronizes session loops without any randomness.
export function staggerForVu(seed, vu) {
  var h = fnv1a32('m43-stagger|' + String(seed) + '|' + String(vu));
  return (h % 50) / 10;
}

// Normal (non-sentinel) products of a manifest tenant record, in manifest
// order. Reads use these for deterministic product-detail picks.
export function normalProductsOf(tenantRecord) {
  var out = [];
  var products = (tenantRecord && tenantRecord.products) || [];
  for (var i = 0; i < products.length; i++) {
    if (products[i].kind === 'normal') out.push(products[i]);
  }
  return out;
}

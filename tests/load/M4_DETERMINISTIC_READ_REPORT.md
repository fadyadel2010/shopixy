# M4.0 Deterministic Single-VU Read Report — Shopixy Load Harness

> Date (UTC): 2026-09-15. Scope: M4.0 only — ONE VU, ONE iteration per
> execution, READ-ONLY storefront journey against the frozen manifest.
> No multi-VU, no ramping/stress/spike/soak/breakpoint, no checkout/POS/
> sale/transactional writes. No production/business code changed. No reseed.
> Manifest untouched. Final verdict: **PASS** (local validation only).

## 1. Manifest validation (fail-closed, in-script `setup()`)

Source of truth: `tests/load/data/manifests/latest.json`
(`seedVersion m2.1`, `verifiedAtUtc 2026-09-15T18:10:04.366458+00:00`).
No tenant/product IDs are duplicated into the k6 script — they are loaded
with `open('../data/manifests/latest.json')` and resolved per VU at runtime.

`setup()` refuses the run unless ALL of these are exactly `true`:

| Flag | Value |
|---|---|
| `projectionReady` | `true` |
| `publicationReady` | `true` |
| `mongoVerified` | `true` |
| `elasticsearchVerified` | `true` |
| `storefrontVerified` | `true` |
| `isolationProbesVerified` | `true` |
| `stockBudgetVerified` | `true` |

Result: all seven `true` on both runs — no refusal. Any absent/false flag
would have triggered `fail('M4 REFUSE TO RUN …')` before any traffic.

Deterministic VU mapping (`k6/lib/manifest.js:tenantForVu`, reused by later
M4 phases): VU 1 → tenant-a, VU 2 → tenant-b, VU 3 → tenant-c, then cycles.
M4.0 additionally asserts the VU is 1 AND the mapped key is `a`, else fail.
Resolved identity (run 001/002): tenant-a
`8242b275-ee28-5e42-b37c-62eb48e2c1b6`, slug `loadtest-tenant-a`,
sentinel `lt-probe-tenant-a` / `LT-PROBE-A`
(`adc0e08b-64e5-59d6-9153-06456aeb5c5c`), normal `lt-a-product-002`
(`b44bc350-3a28-528a-8702-30fe0f9c182f`).

## 2. k6 command (exact, both runs)

```powershell
$env:BASE_URL='http://localhost:5010'
$env:M4_RUN_ID='m4-20260915-001'   # run 2: m4-20260915-002
& "C:\Program Files\k6\k6.exe" run --vus 1 --iterations 1 tests/load/k6/m4-deterministic-read.js
```

k6 v2.2.0. `--vus 1 --iterations 1` on both executions (2 of the permitted
up-to-3 reproducibility runs; a third was not needed). No loop, no flags
above 1. Exit code: run 001 thresholds all `ok:true` (summary JSON);
run 002 process exit `0` (k6 exits nonzero on any threshold breach).

## 3. Route verification + correction vs the smoke design

Before writing the script, every journey route was inspected in its
controller and probed live with the tenant-a Host context. One correction
was required and implemented (no route was invented or substituted):

- **CORRECTION — resolve-slug:** `smoke.js` resolved
  `resolve-slug/{store-slug}`. Tenant slugs are NOT catalog slugs, and the
  phase requires the manifest catalog slug with a positive I-2 assertion.
  `SlugResolverController` (`[Route api/storefront/resolve-slug]`,
  `[HttpGet({slug})]`, product-first then category) was inspected and
  `GET /api/storefront/resolve-slug/lt-probe-tenant-a` was proven live:
  200 `{"type":"product","id":"adc0e08b-…","canonicalSlug":"lt-probe-tenant-a"}`.
  M4.0 resolves the **manifest probe product slug** and asserts
  `canonicalSlug == probeSlug && id == sentinelProductId`.
- **Confirmed, no change:** `GET /api/storefront/store-config`
  (`StorefrontStoreConfigController`), `GET /api/storefront/categories`
  (`StorefrontCategoriesController`), `GET /api/storefront/products` +
  `GET /api/storefront/products/by-id/{id:guid}`
  (`StorefrontProductsController`), `GET /api/storefront/search` +
  `GET /api/storefront/search/suggest`
  (`StorefrontSearchController`: `[Route api/storefront/search]` +
  `[HttpGet("suggest")]` — the smoke path was already correct).
- **Step 9 (safe homepage/catalog read):** M3-B exercised
  products/categories/by-id/search/suggest/resolve-slug but no `home/*`
  endpoint, so the "verified in M3-B" set has no homepage read. The
  discovery-verified safe read `GET /api/storefront/home/featured-products`
  (`StorefrontHomeController`, `[AllowAnonymous]`, Redis-first, read-only)
  was selected and proven live: 200 `[]` (empty — no featured flags are
  seeded; a valid safe read, I-1 trivially passes on `[]`). This is the
  documented step-9 choice, not a substitution: it is the only
  discovery-listed homepage read and it returns 200 under tenant-a context.

## 4. VU / iteration

1 VU, 1 iteration per execution (`options { vus: 1, iterations: 1 }` plus a
runtime `__VU !== 1 → fail` guard). In-script `assertAllowed()` restricts
all 10 GETs to the read-only allowlist; `lib/http.js` throws on any
non-GET method, so a write cannot be issued even by accident.

## 5. Exact endpoint sequence (all anonymous, tenant-a context)

Host `loadtest-tenant-a.localhost:5174` + `X-Tenant-Id: 8242b275-…` on every
request (server re-resolves; response DTO `TenantId` is never authority —
expected identity always comes from manifest + VU mapping).

| # | Endpoint | Run 001 status / latency | Run 002 status / latency |
|---|---|---|---|
| 1 | `GET /api/storefront/resolve-slug/lt-probe-tenant-a` | 200 / 111 ms | 200 / 119 ms |
| 2 | `GET /api/storefront/store-config` | 200 / 52 ms | 200 / 53 ms |
| 3 | `GET /api/storefront/categories` | 200 / 85 ms | 200 / 68 ms |
| 4 | `GET /api/storefront/products` | 200 / 4901 ms | 200 / 2739 ms |
| 5 | `GET /api/storefront/products/by-id/b44bc350-…` (normal) | 200 / 179 ms | 200 / 288 ms |
| 6 | `GET /api/storefront/products/by-id/adc0e08b-…` (sentinel) | 200 / 305 ms | 200 / 327 ms |
| 7 | `GET /api/storefront/search?q=LT-PROBE-A` | 200 / 1544 ms | 200 / 1776 ms |
| 8 | `GET /api/storefront/search/suggest?q=LT-PROBE` | 200 / 411 ms | 200 / 395 ms |
| 9 | `GET /api/storefront/home/featured-products` | 200 / 59 ms | 200 / 343 ms |
| 10 | `GET /api/storefront/products/by-id/a6b0668b-…` (tenant-B sentinel, negative probe) | 404 / 76 ms | 404 / 76 ms |

Journey duration: run 001 7740 ms, run 002 6196 ms. Latencies are recorded
only (`m4_http_latency_ms`, `m4_journey_duration_ms` trends + built-in
`http_req_duration`); no latency threshold was set and no capacity is
claimed. The `products` list is the slowest read on a cold projection path
in this Windows-local/API + Ubuntu-infra topology — an observation, not a
bottleneck claim.

## 6. I-1 result — PASS (both runs)

Every response body (all 10, including the 404 negative-probe body) was
scanned for ~150 foreign identifiers derived from the manifest tenants b/c:
tenantIds, slugs, all 40 productIds/SKUs/slugs, categoryIds/names,
staff/customerIds, probe GUIDs/SKUs/slugs/barcodes/prices/category/customer
fields. `m4_i1_violations`: **0** on both runs. `i1_no_foreign_products`
checks: all pass. No `M4-VIOLATION` line was emitted in either log.

## 7. I-2 result — PASS (both runs, positive assertions)

- resolve-slug: `canonicalSlug == lt-probe-tenant-a` AND
  `id == adc0e08b-64e5-59d6-9153-06456aeb5c5c` — pass
  (`i2_resolve_slug_identity`).
- store-config: `tenantId == 8242b275-ee28-5e42-b37c-62eb48e2c1b6` — pass
  (`i2_store_config_identity`).
- `m4_i2_violations`: **0** on both runs.

## 8. Negative-probe result — PASS (both runs)

Tenant-A context + `GET by-id` tenant-B sentinel product
(`a6b0668b-823f-56b8-81c6-cee6f5b8b5fb`) → **404** (`PRODUCT_NOT_FOUND`),
I-1 scan of the 404 body clean. `m4_negative_probe_failures`: **0**.
This is the single permitted read-only negative probe (counts toward I-1);
no probe matrix was executed.

## 9. Metrics / thresholds

Custom metrics: `m4_http_latency_ms` (trend),
`m4_journey_duration_ms` (trend), `m4_failed_requests`,
`m4_i1_violations`, `m4_i2_violations`, `m4_negative_probe_failures`
(counters). Thresholds: `checks rate==1.0` (23/23 passes, 0 fails, both
runs) and `count==0` on all four counters — all `ok:true` (run 001 summary
JSON) / process exit 0 (run 002). No production SLO claims are made.

## 10. Correlation ID observations

Every request carried a unique UUIDv4 `X-Request-Id` == `X-Correlation-Id`
(`lib/http.js`, `newRequestId()`), plus k6 tags
`{tenant, store, profile=m4-deterministic-read, endpoint,
vu_kind=m4-single, run_id, iteration}` — no secrets in tags (no auth
exists in this journey). The API echoed both IDs in the response headers
(`X-Request-Id` / `X-Correlation-Id` observed on live probes, e.g.
`m4-probe-001`), confirming the edge→API correlation hop from discovery
§6 works under k6 traffic. All 10 request/correlation IDs per run are in
the per-run `.log` (`M4-STEP` lines); all were unique within and across
runs. Mongo/ES projection docs still carry no correlation ID (discovery
§6.2 gap, unchanged — out of scope).

## 11. Violation bundle behavior

On any failure the script emits `M4-VIOLATION` JSON to stderr with:
runId, vuId, iteration, user (`null`, anonymous — recorded as "no user
identity available"), tenant, store, method, url, requestId,
correlationId, statusCode, response hash (FNV-1a + length), foreign IDs
(capped at 20), expected identity, observed identity. Never captured:
tokens, passwords, cookies, secrets (none are sent or stored anywhere in
this phase). **Zero bundles were emitted** — the path is implemented and
exercised by the threshold/counter wiring but was not triggered. A
deliberate negative-path test of the bundle emitter was NOT run (it would
require either a failing assertion against live projections or a synthetic
unit run; both are out of M4.0 scope — noted as a later-phase exercise).

## 12. Safety evidence

- GET-only: `lib/http.js` throws on non-GET; in-script allowlist of 8
  path prefixes covers exactly the 10 issued requests; no other paths.
- No authentication introduced (all 10 endpoints `[AllowAnonymous]`,
  verified in controllers); seeded owner credentials untouched (still
  reserved for later POS/admin scenarios).
- No seeding, no manifest/seed changes, no Mongo/ES direct access, no
  manual Redis clear (`clear-cache` never called), no payment/shipping/
  WhatsApp/email provider calls, no webhooks, no maintenance/debug/
  reindex/dev/test-auth routes.
- Environment observation: `GET /health/ready` returns 503 during this
  phase because `payout_external_truth_readiness = Unhealthy` (finance/
  payout domain, unrelated to storefront reads) with `certificate_provider`
  and `caddy_edge` Degraded (execution disabled — the expected provider-off
  posture). The M4.0 journey does not include health and all 10 storefront
  reads returned their expected statuses, so this is non-blocking context,
  not a verdict input.
- Writes occurred: **none** (10 GETs + server-side read-only serving;
  the only server-side state touched is Redis read-cache behavior owned by
  the API itself, same as any customer browse).
- Providers touched: **none** (payment/shipping reads such as
  `payment-methods` were deliberately not in the 9-step sequence; no intent,
  quote, or lifecycle route was called).

## 13. Per-run artifacts (gitignored raw output)

- `tests/load/results/m4-20260915-001.log` — full k6 console output, run 1
- `tests/load/results/m4-m4-20260915-001-summary.json` — handleSummary metrics
- `tests/load/results/m4-20260915-002.log` — full k6 console output, run 2
- `tests/load/results/m4-m4-20260915-002-summary.json` — handleSummary metrics
- `tests/load/results/.gitignore` — `*` + `!.gitignore`: raw per-run
  artifacts stay local-only; the committed record is this report plus the
  (committed) k6 scenario and lib files.

## 14. Final verdict — PASS

Manifest green, 1 VU / 1 iteration per execution, exact 9-step read journey
+ 1 negative probe all at expected statuses, I-1 = 0 violations, I-2
positive assertions green, negative probe 404, correlation IDs unique and
echoed, zero violation bundles, zero writes, zero provider touches, two
reproducibility runs with identical outcomes.

Explicitly NOT claimed: capacity numbers, latency SLOs, full tenant
isolation certification (single-tenant, single-VU reads only; I-3…I-9 and
concurrent multi-tenant scenarios remain later phases). Stop after M4.0 —
M4.1 not started.

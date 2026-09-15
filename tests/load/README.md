# Shopixy Load Harness — M1.1 Skeleton (READ-ONLY)

## Purpose

Minimal runnable skeleton proving k6 integration, deterministic per-VU
identity, tenant-aware request construction, per-request correlation IDs,
consistent k6 tags, fixture-driven isolation assertions, and a fail-closed
safety preflight — with a 1-VU read-only Stage-0 smoke. Design authority:
`tests/load/LOAD_TEST_DISCOVERY.md`. No generators, seeders, checkout,
stress, or soak in this phase.

## Prerequisites (Windows local)

- k6 binary on PATH (`k6 version`)
- Python 3.10+ (stdlib only; `python --version`)
- Node.js optional (only for `node --check` syntax validation; k6 runs the scripts)
- API running locally + infrastructure reachable; nothing in M1.1 requires seed data

## Environment variables

| Var | Required | Example |
|---|---|---|
| `BASE_URL` | yes | `http://localhost:5000` |
| `TEST_ENVIRONMENT` | yes | `local` (`local\|test\|ci\|staging-test`) |
| `TEST_TENANT` | yes | tenant GUID under test |
| `TEST_SLUG` | yes | `store-a` |
| `TEST_PRODUCT_ID` | yes | deterministic product id/slug |
| `TEST_DB_PROOF` | yes (preflight) | `test-db-local-2026-09-15` (never a prod name) |
| `TEST_HOST_OVERRIDE` | no | `ahramstore.localhost:5174` (defaults to `{TEST_SLUG}.localhost:5174`) |

Anonymous storefront reads resolve the tenant from the `Host` header via
`Storefront:UrlTemplate` (`TenantResolutionMiddleware` strips inbound
`X-Tenant-Id`; a bare `localhost:PORT` Host yields `400 TENANT_REQUIRED`).
The harness therefore sends `Host: {slug}.localhost:5174` while the TCP
connection still targets `BASE_URL`. Port 5174 is template-sensitive
(M1.2 finding); override only via `TEST_HOST_OVERRIDE` when the template differs.

Never commit secrets. No tokens/passwords are used or logged in M1.1.

## Read-only nature of M1.1

`k6/smoke.js` issues GET only (`http.js` throws on any other method).
It never creates orders/sales, reserves inventory, calls payment/WhatsApp/
email/shipping providers, touches maintenance/debug/reindex/dev/test-auth
routes, or writes to any database. `orchestrate.py` refuses to run if
`LOAD_SMOKE_WRITES` is set, if provider execution flags are enabled, or if
any production hostname marker is detected.

## Exact smoke flow (1 VU, 1 iteration)

1. `GET /health/ready`
2. `GET /api/storefront/resolve-slug/{slug}` (+ I-2 store-resolution check)
3. `GET /api/storefront/store-config`
4. `GET /api/storefront/categories`
5. `GET /api/storefront/products` (+ I-1 foreign-tenant check)
6. `GET /api/storefront/products/by-id/{TEST_PRODUCT_ID}` (+ I-1)
7. `GET /api/storefront/search?q=probe` (+ I-1)
8. `GET /api/storefront/search/suggest?q=pro`

Every request carries `X-Request-Id` + `X-Correlation-Id` (unique per
request) and tags `{tenant, store, profile, endpoint, vu_kind}`.

## Safety model

`python/orchestrate.py` (preflight only) fails closed unless: all required
env present; `TEST_ENVIRONMENT` in allowlist; `BASE_URL` host is local or
non-production; smoke path list contains no denylisted route; no provider
execution flags enabled; explicit `TEST_DB_PROOF` present and non-prod;
write mode not requested. On any failure: `PREFLIGHT REFUSE` + nonzero exit,
no remediation, no traffic.

## Deterministic VU identity

`k6/lib/identity.js`: VU 1→tenant-a/user-a-001, VU 2→tenant-b/user-b-001,
VU 3→tenant-c/user-c-001, cycling. Stable GUIDs/slugs; per-VU session ids;
per-request UUID-format correlation ids. The server re-resolves
`X-Tenant-Id` (`tenant.js` sends it; response DTO `TenantId` is never
authority — expected identity comes from VU mapping/env).

## Why k6 is the primary load generator

Per discovery §11: single Go binary on Windows, low VU overhead, git-friendly
JS, native stages/thresholds/tags, CI-friendly exit codes, decoupled
off-host execution. Python never generates traffic — it plans data
(`seed_plan.py`), gates runs (`orchestrate.py`), and will later analyze/
validate results.

## What Python does and does not do

- `seed_plan.py`: prints/writes a deterministic universe plan
  (`universe.json`/`probes.json`, marked NON-PRODUCTION TEST DATA). No DB
  writes, no network, no credentials.
- `orchestrate.py`: preflight gate only. No traffic, no seeding, no remediation.
- Neither file seeds/mutates PostgreSQL/MongoDB/Redis.

## Local validation vs later K8s capacity testing

**Local Windows + remote Ubuntu infrastructure results are NOT production
capacity claims.** M1.1 validates harness correctness and isolation probes
only. Staged capacity/stress/soak characterization happens later in an
isolated perf/K8s environment with exporters and dashboards.

## First safe smoke run

```powershell
$env:BASE_URL='http://localhost:5000'
$env:TEST_ENVIRONMENT='local'
$env:TEST_TENANT='<tenant-guid>'
$env:TEST_SLUG='<store-slug>'
$env:TEST_PRODUCT_ID='<product-id-or-slug>'
$env:TEST_DB_PROOF='test-db-local-<date>'
python tests/load/python/orchestrate.py
# only if PREFLIGHT OK:
k6 run --vus 1 --iterations 1 tests/load/k6/smoke.js
```

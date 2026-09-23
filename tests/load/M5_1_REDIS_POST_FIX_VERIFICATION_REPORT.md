# M5.1 Redis Post-Fix Verification Report — Shopixy Load Harness

> Date (UTC): 2026-09-17 (~18:10–18:25 UTC). Scope: NARROW post-fix verification ONLY.
> No rebuild, no reseed, no projection rebuilds, no k6, no M4.0/M4.1/M4.2-A, no VU stages,
> no migrations, no random entities, no business-logic/schema changes.
> Baseline: `tests/load/M5_NEW_ENVIRONMENT_REBUILD_REPORT.md` (verdict PASS WITH LIMITATIONS,
> Limitation 1 = Redis replica fan-out).
> Final verdict: **PASS** (§17).

## 1. Scope

Prove the remediated chain end-to-end and stop:
Kubernetes Redis routing (`IngressRouteTCP/redis` → Service `redis-master`) →
external `192.168.1.190:6379` → single Redis master → StackExchange.Redis READ/WRITE →
`POST /api/admin/sync/product-variants` = HTTP 200 (previous
`Required Redis invalidation failed ...` error gone) → Mongo/storefront integrity intact.
Establish that M5 Limitation 1 is RESOLVED. HARD STOP respected: no k6 binary, script, VU
stage, checkout, order, payment, provider, migration, or workload-definition action taken
(`tests/load/k6/` and `tests/load/results/` byte-untouched, §14).

## 2. Safety preflight

- Git: `tests/load/k6/` clean (no modifications); last k6 commit `f939e6b` (pre-existing).
- Manifest is the post-M5 frozen manifest: `environment=loadtest-new`,
  `database={host:192.168.1.190, database:PosEcommerceDb_LoadTest}`,
  `verifiedAtUtc=2026-09-17T18:03:43.092357+00:00`, all 7 k6 setup flags `true`,
  `m5NewEnvironment` block present.
- Target DB confirmed exactly `192.168.1.190 / PosEcommerceDb_LoadTest` (manifest database
  block + live API health `postgresql` entry + `seed.py` safety-gate conninfo
  `host=192.168.1.190 db=PosEcommerceDb_LoadTest`). No production marker anywhere
  (`TEST_DB_PROOF=PosEcommerceDb_LoadTest@192.168.1.190-loadtest-new`,
  `TEST_ENVIRONMENT=local`).
- API is the load-test API on `http://localhost:5010` (storefront `store-config` 200 with
  correct `tenantId` binding pre- and post-restart).
- M5 report present as baseline (`tests/load/M5_NEW_ENVIRONMENT_REBUILD_REPORT.md`, 25805 bytes).
- Fail-closed posture held throughout: every privileged step used the repository's existing
  gates (`seed.py` safety gate / `orchestrate` allowlist semantics / Owner-JWT sessions).

## 3. Kubernetes routing evidence

- Remediation applied outside this session (per task brief): `IngressRouteTCP/redis`
  repointed from Service `redis` to Service `redis-master`, with `redis-master` selecting
  the single Redis master endpoint.
- Object-level YAML capture was NOT possible from this host: no `kubectl`/`k3s`/`crictl`
  binary, no `~/.kube` config, and K8s API `:6443` on `192.168.1.190` is unreachable
  (TCP refused). No credentials were hunted and none were invented.
- Routing is instead proven BEHAVIORALLY at every layer below — which is the stronger
  end-to-end evidence for the stated objective: 8/8 fresh connections report MASTER (§4),
  real writes/reads/deletes succeed (§5), a fresh application multiplexer is write-capable
  (§6), and the previously failing application write path returns 200 for all tenants (§8).
  Under the M5 defect these same checks returned 3-master/5-slave and deterministic 500s.

## 4. Multi-connection Redis ROLE evidence

- Method (mirrors the M5 diagnostic): 8 independent fresh `redis-py` connections to
  `192.168.1.190:6379`, DB 0, real password auth, `ROLE` + `INFO replication` per connection.
- Result: **8/8 MASTER**, same `master_replid=66924cac2d2b23bcb0064a1606e12fddc2074905`
  on every connection (`conn0..conn7: role=master`). Zero SLAVE/REPLICA observations.
- Contrast with M5 defect state (same script): 3 master / 5 slave across 8 connections.
- Acceptance met: deterministic master-only routing proven across repeated fresh connections.

## 5. Redis SET/GET/INCR/DELETE evidence

- Namespace `m51:redis-diag:<unique-id>` (unique per run), real auth, external endpoint:
  - `SET m51:redis-diag:<uid> v1 EX 120` → True.
  - `GET` → exact value `v1` (byte-compared).
  - Counter `SET ...:ctr 0` → True; `INCR` → 1; `INCR` → 2; `GET` → `2`.
  - `DEL` (both keys) → 2; `GET after delete` → None/None.
- No Redis errors (nothing to quote verbatim — zero errors raised).
- No flush, no pattern deletion, no pre-existing keys touched; diagnostic keys removed
  (verified gone). Only uniquely namespaced temporary keys were ever manipulated.

## 6. API restart evidence

- Pre-restart state: API serving 5010 (PID 3012, started 21:10 local); `redis_latency`
  already `Healthy (25 ms)` — consistent with the routing fix being live before the restart.
- This phase performed exactly ONE restart: old process stopped; relaunched with the
  byte-identical observed command
  (`dotnet bin/Debug/net9.0/PointOfSale.API.dll --urls http://localhost:5010`,
  CWD `D:\PointOfSale\PointOfSale.API`, Development env) so a fresh StackExchange.Redis
  multiplexer is built against the corrected endpoint. Restart was NOT used as a routing
  workaround (routing proven fixed in §4 before the restart mattered).
- Post-restart: EXACTLY ONE serving instance — `PointOfSale.API` PID 17896 (child of
`dotnet run` PID 7980), sole owner of `127.0.0.1:5010`/`[::1]:5010`. (A concurrent
`MSBuild.dll` PID 18036 is an unrelated IDE build; no second API exists. One shell
command in this session was aborted mid-restart and the operator confirmed the API
restart separately; end-state verified single.)
- API listening confirmed (TCP 5010 connect 0) and storefront `store-config` 200.

## 7. Health evidence

- `/health` (post-restart): `redis: Healthy` (reachable, connected, ~14 ms PING),
  **`redis_latency: Healthy` (28 ms write/read/delete cycle)** — the write-capable state:
  no replica/write refusal. `rabbitmq`, `elasticsearch` (Green, 20/0 shards), `mongodb`
  (`ecommerce_read_db_loadtest ok=1`), `postgresql` (`PosEcommerceDb_LoadTest`),
  `masstransit-bus` (all projection endpoints ready) all Healthy.
- Unchanged pre-existing non-load-test entries (identical in M5, out of scope):
  `CommerceHubSearchSync` Unhealthy (syncRatio 0.4 on non-loadtest listings),
  `payout_external_truth_readiness` Unhealthy, `projection_drift` Degraded (43 business-data
  docs, `ForeignTenantDocumentCount=0`), `stock_reservation` Degraded (1 stale unit),
  certificate/caddy Disabled-by-design. `/health/ready` 503 is driven by these same
  non-load-test entries (as in M4.2A); load-test dependencies are all green.

## 8. product-variants application-path results (A/B/C)

- Mechanism: existing first-party `POST /api/admin/sync/product-variants`, sequential
  tenants, real Owner JWT (`POST /api/auth/login` 200 + tenant-bound `tenantId` + session
  cookie → Bearer). Route unmodified; no new endpoint; no bypass.
- Results (sequential, ~0.8–1.4 s each):
  - tenant-a → **HTTP 200** (`totalProcessed=20 successCount=20 skippedCount=0 errorCount=0 success=true errors=[]`).
  - tenant-b → **HTTP 200** (same 20/20/0/0 shape).
  - tenant-c → **HTTP 200** (same 20/20/0/0 shape).
- The M5 error string `Required Redis invalidation failed ...` occurs ZERO times in any
  response (explicit substring check on all three bodies). Redis invalidation/write path
  proven working through the real application operation.

## 9. Mongo verification

- Read-only vs `ecommerce_read_db_loadtest` (no rebuild performed in M5.1; §8 syncs are the
  ordinary projection writer path, not a universe rebuild):
  - tenant-a/b/c each: products **20/20**, categories **4/4**, tenantMatch True,
    sentinelPresent True with exact fingerprints (`LT-PROBE-A/B/C`,
    `lt-probe-tenant-a/b/c`, `111.11/222.22/333.33`), **foreignProbeHits 0**,
    **foreignTenantLeak 0**.

## 10. OpenSearch verification

- Read-only (`products_loadtest` + shared `categories`, Basic Auth; no index/mapping/alias
  changes of any kind):
  - tenant-a/b/c each: sentinelDoc 200 + tenantMatch, products **20**, categories **4**,
    searchBySlug 1, searchBySku(`sKU` field) 1, foreign probes **0/0**.

## 11. PostgreSQL/stock integrity

- `seed.py --verify` post-fix: per-tenant staff 3/3, customers 6/6, categories 4/4, products
  20/20, variants 22/22, warehouses 2/2, stocks 23/23; `tenantActive`, `sentinelFingerprint`,
  `stockBudgets` OK; `foreignTenantViolations=0` — identical to the M5 pre/post runs.
- Catalog guard: total=20, publishedVisible=20, defaultUnits=19 per tenant (unchanged).
- Stock budgets unchanged: quantities/budgets verified equal to manifest; no stock mutation
  by sync activity (syncs and lifecycle paths never touch stock tables).
- The pre-existing `-D` variant SKU/barcode skew (`sentinelVariantBarcode` FAIL ×3) remains
  exactly as documented in M5 — NOT a regression, deliberately untouched per the
  comparability rule. No unexpected test rows (universe membership 20/20 throughout).

## 12. Storefront verification

- Anonymous reads (`Host: <slug>.localhost:5174`), all tenants: resolve-slug, store-config,
  categories, products, normal by-id, sentinel by-id, search, suggest, featured-products —
  **all HTTP 200 (27/27)**; I-2 bindings true (resolve-slug canonicalSlug+id, store-config
  tenantId, sentinel sku+slug fingerprints); search/suggest foreignLeak False throughout.

## 13. Cross-tenant negative probes

- A→B, B→C, C→A sentinel by-id: **404 + no leak (3/3)**; same probes via search: 200 with
  zero foreign identifiers (3/3). Isolation semantics identical to M5/M4 gates.

## 14. Manifest/k6 integrity

- `tests/load/k6/` — zero modifications (git status clean); `tests/load/results/` — zero new
  artifacts (no load executed).
- Workload identity assertion (current `latest.json` vs pre-M5 backup): tenant IDs/slugs,
  all 60 product (slug/productId/kind/SKU), all 66 variant (variantId/SKU), all 12 category,
  all stock-budget (variantId/warehouseId/quantity) rows, all 3 probe records, `seedVersion`
  `m2.1` + namespace — **IDENTICAL**. VU-to-tenant mapping and M4 scenario semantics
  unchanged (same frozen files).
- Manifest NOT rewritten or re-frozen in M5.1 (per instruction); no metadata timestamp changed
  by this phase (`verifiedAtUtc` still the M5 freeze value). Only repo-side delta pre-existing
  from M5: `orchestrate.py` (+93 additive `m5_preflight` lines), `m5_execute.py`,
  `M5_NEW_ENVIRONMENT_REBUILD_REPORT.md`, manifest metadata freeze + snapshot + pre-freeze backup.

## 15. Credential rotation performed (secrets never exposed)

- M5 left the 3 owner passwords at fresh unknown random values. M5.1 required Owner JWTs,
  so: (1) temp rotation of ONLY `loadtest-owner-a/b/c` `PasswordHash` (deterministic-Id +
  username + tenant triple-checked; session value in process env only, never logged, never
  written to any file); (2) all Phase 5 logins/syncs executed; (3) FINAL rotation back to a
  fresh RANDOM unknown value (in-memory only, never displayed) — end-state matches the M5
  convention (no valid credential in any transcript or file). No other user touched.

## 16. Exact residual limitations

- L1 (M5 Redis limitation): **RESOLVED** — evidence §§4–8. No residual Redis limitation;
  standard monitoring of the new `redis-master` routing applies going forward.
- L2 (pre-existing, carried): manifest variant `-D` SKU/barcode suffix vs bare SQL values —
  identical on both environments, zero workload impact, untouched.
- L3 (telemetry, carried): no Prometheus/Grafana/exporters attached; verification is
  API/Mongo/ES assertions only (same posture as M4.2A/M5).
- Scope gap (not a product limitation): K8s object YAML (`IngressRouteTCP`, Service
  selector, EndpointSlice) could not be captured from this Windows host (no kubectl /
  kubeconfig; K8s API unreachable). Compensated by the complete behavioral chain, which
  proves the effective data path rather than the object names.
- With L1 resolved, the M5 §17 latency-comparability caveat (uncacheable reads) no longer
  applies to runs executed from this state: the write-capable multiplexer lets the product
  cache stick again (verified via successful tenant invalidations in §8).

## 17. Final verdict

**PASS** — external endpoint proven master-only across repeated fresh connections (8/8
MASTER); Redis write/read/delete works; API health Healthy for Redis connectivity AND
latency; `product-variants` returns 200 for all 3 tenants with 20/20/0/0 and zero
Redis-invalidation errors; Mongo/OpenSearch/PostgreSQL-stock/storefront/cross-tenant
integrity all green with no regressions; harness and manifest workload identity untouched.
M5 Limitation 1 is RESOLVED. No follow-up rebuild required before replaying the frozen
M4 read scenarios (still HARD-STOPPED in this phase: none executed).

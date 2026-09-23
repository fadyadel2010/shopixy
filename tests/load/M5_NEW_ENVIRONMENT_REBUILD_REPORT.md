# M5 New-Environment Rebuild Report — Shopixy Load Harness

> Date (UTC): 2026-09-17T18:03:43+00:00 (rebuild executed 2026-09-17 ~17:30–18:03 UTC).
> Scope: M5 rebuild only — deterministic universe reconstruction + projection rebuild +
> read-only verification on the NEW infrastructure. No k6 load, no M4.0/M4.1/M4.2-A,
> no VU stages (no 50/100/250 VU), no orders/sales/payments, no providers.
> Methodology: EXACT M2 → M3-B sequence replayed unchanged (same seed machinery, same
> application paths, same routes, same VU-mapping/assertion contracts in the frozen k6 files).
> Final verdict: **PASS WITH LIMITATIONS** (§19).

## 1. New infrastructure

- Windows: ASP.NET Core Load-Test API `http://localhost:5010`
  (`dotnet bin/Debug/net9.0/PointOfSale.API.dll --urls http://localhost:5010`,
  `ASPNETCORE_ENVIRONMENT=Development`, PID 17624 observed) + k6 (installed, **NOT run** this phase).
- Kubernetes / Ubuntu `192.168.1.190`:
  - PostgreSQL `:5432`, database `PosEcommerceDb_LoadTest` (user `shopixy`).
  - Redis `:6379`, DB 0, instance `pos-loadtest` — **see §13 limitation: endpoint fans out
    to 1 master + 2 replicas; only `:6379` is exposed, no master-only/sentinel port.**
  - RabbitMQ `:5672`, vhost `shopixy` (load-test config `loadtest-windows` is the
    `CommerceHubProjectionEndpointPrefix`, not a vhost).
  - MongoDB `:27017` (replica set `rs0`, `directConnection=true`), database
    `ecommerce_read_db_loadtest`.
  - OpenSearch `https://192.168.1.190:9200` (Basic Auth `admin`, self-signed cert pinned
    by fingerprint in user-secrets; cluster status Green, 20 active / 0 unassigned shards).
- Load-test OpenSearch indexes observed on the new cluster:
  `products_loadtest` (0 docs pre-rebuild → populated §6),
  `commercehub_products_loadtest` (alias) → concrete `commercehub_products_loadtest_v2`
  (6 docs, pre-existing non-loadtest content, untouched),
  `public_affiliate_products_loadtest` (0 docs, untouched),
  plus shared `categories` / `cmspages` (0 docs pre-rebuild on this cluster; tenant-filtered).
- API runtime config already targeted the new infra before M5
  (`appsettings.Development.local.json`: Postgres/Mongo/Redis/RabbitMQ/OpenSearch all
  `192.168.1.190` + loadtest names), so `seed.py` with `TEST_ENVIRONMENT=local` resolves to
  the NEW database with no connection-string override and no gate weakening.

## 2. Database target

- Host `192.168.1.190`, database `PosEcommerceDb_LoadTest` — NEW approved target.
  `TEST_DB_PROOF=PosEcommerceDb_LoadTest@192.168.1.190-loadtest-new` (contains actual host+db).
- NOT used: `192.168.1.5`, `PosEcommerceDb_Baseline` (referenced read-only once, §16, to prove
  the variant-barcode skew predates this migration; never written).
- EF migrations: NOT run (schema pre-provisioned; 276 tables; all M2 tables present:
  `Tenants/Users/OnlineCustomers/Categories/Products/ProductVariants/Warehouses/
  ProductVariantStocks/StoreConfigs/ProductUnits`).
- Safety: `TEST_ENVIRONMENT=local`; new `orchestrate.m5_preflight()` (same strictness as
  `m3b_preflight`, only the approved host/db constants differ; `m3b_preflight` untouched);
  prod markers refused; TestAuth/bypass forbidden; no bypass flag exists. `M5 PREFLIGHT OK` logged.
- Other tenants in the new DB (`ahramstore`, `antonstore`, `antonnewstore`, `greatone`,
  `highquality`, `shopixy`) preserved untouched; seeder never deletes.

## 3. Seed result

- M2 seed machinery (`tests/load/python/seed.py`, deterministic UUIDv5 `seedVersion m2.1`,
  namespace `b9c4d3a1-7e22-4f5a-9c1d-2a6f8e0b4d11`) used as identity authority.
- The new DB already carried the full deterministic namespace (clone of post-M3B state), so
  `seed.py --verify` semantics applied; `--seed` writes were NOT needed and NOT executed
  (would have been idempotent no-ops for IDs, and would have stripped the 7 projection flags
  from `latest.json`, so correctly avoided). Safety gates never weakened.
- `seed.py --verify` vs `192.168.1.190/PosEcommerceDb_LoadTest`, run BEFORE and AFTER all M5
  activity — identical results both times:
  - per tenant: staff 3/3, customers 6/6, categories 4/4, products 20/20, variants 22/22,
    warehouses 2/2, stocks 23/23 → totals 9 / 18 / 12 / 60 / 66 / 6 / 69 + 3 sentinels. ALL OK.
  - `tenantActive` OK ×3; `sentinelFingerprint` (product SKU/slug/barcode/price) OK ×3;
    `stockBudgets` OK ×3; `foreignTenantViolations=0` ×3.
  - `sentinelVariantBarcode` FAIL ×3 — PRE-EXISTING skew, not a regression (see §16):
    manifest records default-variant SKU/barcode with `-D` suffix (`LT-PROBE-A-D`,
    `LTBCPROBEA001-D`), while SQL carries the bare SKU (`LT-PROBE-A`, `LTBCPROBEA001`).
    Proven identical on the OLD baseline (`192.168.1.5/PosEcommerceDb_Baseline` queried
    read-only: same bare values). Variant UUIDs + stock budgets match exactly; k6 workload
    consumes product-level IDs/SKUs/slugs only (unaffected). Seeder preserves by Id and never
    overwrites, so left as-is per the comparability rule.
- No random users/products created; frozen identity model unchanged; no new workload model.

## 4. StoreConfig/publication result

- Method (M3-B + `M3B_STORECONFIG_PATH_AUDIT.md`, unchanged): `GET /api/admin/store-config`
  → `PUT /api/admin/store-config/status` (`storefrontStatus=live` + RowVersion), real tenant
  Owner JWT (`POST /api/auth/login`, 200 + `access_token` cookie + body `tenantId` binding,
  per-tenant, sequential A→B→C). Registration NOT used; no SQL INSERTs.
- Pre-rebuild SQL showed all 3 loadtest tenants already `StorefrontStatus=live`
  (`StoreName "My Store"`); M5 re-verified through the application path:
  `GET /api/admin/store-config` → 200 with `tenantId` bound per tenant; publication
  re-verify → `live` + matching `tenantId` (already-live path, no blind writes).
- Owner authentication: real logins for `loadtest-owner-a/b/c` (200, Owner role, tenant-bound).
  Passwords: (i) the first M5 executor attempt rotated the 3 owner hashes to a fresh random
  value (field-only, deterministic-Id-scoped, same authorized pattern as M3-B); (ii) diagnosis
  rotated to a known session value; (iii) final rotation set a fresh RANDOM value known to
  nobody (in-memory only, never logged/stored) — so no credential lingers in any transcript.
  Only `Users.PasswordHash` of the 3 loadtest owners was ever written; no other rows.
- Unit backfill (M2-deferred prerequisite, organic path): `POST /api/units/seed-defaults` +
  per-product base-Piece attach already complete in this DB (SQL guard: `defaultUnits=19`
  per tenant before and after M5; backfill checks report already-present, zero attaches).
- SQL catalog end-state guard (read-only, after ALL M5 activity): per tenant
  `total=20 publishedVisible=20 defaultUnits=19` (variant-container excluded by design).

## 5. Mongo result

- Mechanism (M3-B first-party tenant-scoped, unchanged): `POST /api/admin/sync/categories`
  + `POST /api/admin/sync/product-variants` per tenant Owner JWT. No direct Mongo inserts.
- `POST /api/admin/sync/categories`: 200 ×3 (`count=4`, `staleDeleted=0`, correct `tenantId`).
- `POST /api/admin/sync/product-variants`: HTTP 500 ×3 with
  `Errors=["Required Redis invalidation failed for
  'pos-loadtest:tenant:<id>:cache-generation:storefront-products'"]`
  — the failure is the TRAILING required Redis invalidation (§13); the Mongo rebuild
  inside the same handler COMPLETED before the throw. Verification (below) proves the
  Mongo end-state is fully achieved despite the 500 status.
- Mongo verification (`ecommerce_read_db_loadtest`, read-only) — ALL GREEN, all tenants:
  - tenant-a: products 20/20, categories 4/4, tenantMatch True, sentinelPresent True
    (`LT-PROBE-A` / `lt-probe-tenant-a` / `111.11`), foreignProbeHits 0, foreignTenantLeak 0.
  - tenant-b: products 20/20, categories 4/4, tenantMatch True, sentinelPresent True
    (`LT-PROBE-B` / `lt-probe-tenant-b` / `222.22`), foreignProbeHits 0, foreignTenantLeak 0.
  - tenant-c: products 20/20, categories 4/4, tenantMatch True, sentinelPresent True
    (`LT-PROBE-C` / `lt-probe-tenant-c` / `333.33`), foreignProbeHits 0, foreignTenantLeak 0.
- Pre-rebuild Mongo held 0 products / 0 categories (all other collections likewise empty
  except 10 `orders` rows and 6 `commercehub_products` docs unrelated to loadtest, untouched).

## 6. OpenSearch result

- Mechanism (M3-B tenant-scoped first-party, unchanged): `POST /api/admin/sync/search/full`
  per tenant. No index delete/recreate, no alias promote, no global reindex.
- `POST /api/admin/sync/search/full`: 200 ×3 (tenant-a 948 ms, tenant-b 833 ms,
  tenant-c 1374 ms), each `products: totalProducts=20 successCount=20 failureCount=0`,
  `categories: totalCategories=4 successCount=4`, `cmsPages: totalPages=0` (no CMS pages
  seeded by design), `errors=[]`.
- Projection wait window respected: 60 s per tenant before assertion (M3-A/M3-B guidance).
- OpenSearch verification (direct `_doc`/`_count`/`_search` with Basic Auth, read-only) —
  ALL GREEN, all tenants (index `products_loadtest`; categories in shared `categories` index,
  tenant-filtered; note the SKU field is `sKU` — queries used the doc-detected field name,
  same as the M3-B verifier):
  - tenant-a: sentinelDoc 200 + tenantMatch, productsCount 20, categoriesCount 4,
    searchBySlug 1, searchBySku(`sKU`) 1, foreignProbeB/C 0/0.
  - tenant-b: sentinelDoc 200 + tenantMatch, productsCount 20, categoriesCount 4,
    searchBySlug 1, searchBySku 1, foreignProbeA/C 0/0.
  - tenant-c: sentinelDoc 200 + tenantMatch, productsCount 20, categoriesCount 4,
    searchBySlug 1, searchBySku 1, foreignProbeA/B 0/0.
- Pre-rebuild: `products_loadtest` 0 docs; post-rebuild tenant-filtered counts above;
  `commercehub_products_loadtest[_v2]` / `public_affiliate_products_loadtest` untouched.

## 7. Organic event-path result

- M3-B spot-check repeated (2 ordinary products per tenant, same slugs):
  unpublish → real workflow/event path → Mongo-visibility poll → publish → poll → verify.
- First executor attempt's polls timed out under then-current conditions (bus backlog after
  back-to-back syncs); clean sequential re-run: **6/6 converged, ~5.0–5.1 s per leg**
  (identical to M3-B's 5.0–5.2 s lags):
  - tenant-a `lt-a-product-002`: unpub 200 (Processed) lag 5.1 s, pub 200 lag 5.0 s.
  - tenant-a `lt-a-product-003`: unpub 200 lag 5.0 s, pub 200 lag 5.0 s.
  - tenant-b `lt-b-product-002`: unpub 200 lag 5.0 s, pub 200 lag 5.0 s.
  - tenant-b `lt-b-product-003`: unpub 200 lag 5.0 s, pub 200 lag 5.0 s.
  - tenant-c `lt-c-product-002`: unpub 200 lag 5.0 s, pub 200 lag 5.0 s.
  - tenant-c `lt-c-product-003`: unpub 200 lag 5.0 s, pub 200 lag 5.0 s.
- Notably the organic `bulk/publish|unpublish` path returns 200 (its Redis usage is
  best-effort/async via the Healthy `product-cache-invalidation` / `redis-invalidation`
  bus endpoints), proving outbox → bus → Mongo projection delivery end-to-end on new infra.
- Universe unchanged: final SQL guard 20/20 Published per tenant (§4); no test rows added.

## 8. Storefront result

- Anonymous read validation only (`Host: <slug>.localhost:5174` + `X-Tenant-Id`), all tenants —
  ALL 200, ALL GREEN:
  - tenant-a/b/c each: resolve-slug 200 (I-2 identity True: `canonicalSlug` + `id` match
    manifest sentinel), store-config 200 (I-2 `tenantId` True), categories 200, products 200,
    by-id normal 200, by-id sentinel 200 (fingerprint `sku`+`slug` match), search 200
    (foreignLeak False), suggest 200 (foreignLeak False), featured-products 200.
- Read path is Redis-first but fail-open on the replica (§13): with an empty/unstickable
  cache every read recomputes from SQL/Mongo authority and still returns 200 — correctness
  holds; latency comparability caveat recorded in §17.

## 9. I-1 (no foreign tenant data)

- **I-1 = 0.** Storefront search/suggest bodies (query-echo stripped) contain zero foreign
  probe identifiers on all tenants; Mongo `foreignProbeHits=0` and `foreignTenantLeak=0`
  on all tenants; ES foreign-probe searches return 0 hits in all 6 directions.

## 10. I-2 (identity binding)

- **I-2 = 0.** resolve-slug `canonicalSlug`+`id` and store-config `tenantId` bind to the
  manifest tenant on all 3 tenants; sentinel by-id fingerprints match; no null-identity or
  wrong-tenant bundle observed (no load in this phase, so no error-body character to classify).

## 11. Cross-tenant negative probes

- A→B, B→C, C→A sentinel by-id: **404 + no leak, all 3** (M4 gate semantics preserved).
- Same probes via search: 200 with zero foreign identifiers leaked, all 3.

## 12. Stock budget comparison

- `seed.py --verify` BEFORE vs AFTER all M5 activity: identical — `stockBudgets` OK ×3,
  all (variant, warehouse) quantities equal manifest (`sentinel 100.00`, `plentiful 10000.00`
  / `250.00` w2 sample, `low-stock 3.00`, `contended 20.00`, explicit `0.00` rows,
  `container-default 500.00/100.00`, `ReservedQuantity=0` throughout; untracked products
  correctly have no rows). 69 rows per full universe; quantities never mutated by
  StoreConfig/Mongo/ES/organic-path work (syncs and lifecycle transitions do not touch stock).
- Stock budgets therefore remain exactly the expendable caps the k6 contended-SKU schedules
  were validated against (expect 409s on contended SKU by design — unchanged).

## 13. Redis behavior

- Passive use only by the harness: no manual clear/flush, no `clear-cache` endpoint, no
  pattern deletes by hand. Projection-writer invalidation relied upon (as in M3-B).
- **Limitation 1 (blocker-class infra defect, precisely root-caused):**
  `192.168.1.190:6379` is NOT a single master — 8 fresh connections returned
  `ROLE=master,master,slave,slave,master,slave,slave,master` pattern (3 master / 5 slave;
  same `master_replid`; slaves `10.42.0.14`, `10.42.0.39`): the endpoint round-robins across
  1 master + 2 replicas. Consequences, all evidenced:
  - StackExchange.Redis (API + any .NET client with the shipped options) refuses writes on
    replica connections: `No connection (requires writable - not eligible for replica)` —
    reproduced in an isolated probe with the exact connection string/options (PING 3–4 ms OK,
    `SETEX`/`INCR` refused). The API's singleton `Lazy` multiplexer (pinned at process start
    20:17 local) sits on a replica → EVERY required Redis write fails, deterministically:
    `POST /api/admin/sync/product-variants` → 500
    (`Required Redis invalidation failed for 'pos-loadtest:tenant:<id>:
    cache-generation:storefront-products'`, 0.6 s duration), and `redis_latency` health =
    `Degraded | Redis unavailable`, while `redis` (PING) = `Healthy (6.19 ms)`.
  - Old environment contrast: `192.168.1.5:6379` is Redis 6.0.16 standalone master → the
    identical code path returned 200 in M3-B. New endpoint is Redis 7.0.15 HA pool.
  - Blast radius: (a) tenant product-sync STATUS is 500 (Mongo writes inside still apply —
    §5 end-state green); any future re-sync returns 500 until fixed. (b) Read path is
    fail-open (proven 200s in §8) but the cache can never stick while pinned to a replica,
    so every read recomputes — M4 latency/throughput comparisons vs M4.2A MUST carry this
    caveat. (c) No master-only or Sentinel port is exposed (probed 6380/6381/26379/26380:
    refused); remediation is K8s-side (master-only Service for `pos-loadtest`, or route the
    instance at a stable master). Restarting the API alone is a 1-in-3 lottery, NOT a fix —
    deliberately not attempted.
  - Disclosure: one diagnostic `INCR` on tenant-a's generation key landed on the master
    (value `1`, equivalent to a single natural invalidation advance — cache-miss direction
    only, never a stale hit) plus short-lived `pos-loadtest:diag-*` keys (created and
    deleted; the .NET probe's `diag-dotnet-probe2` SET never reached the server — refused
    client-side, nothing to clean). No loadtest projection data was altered.

## 14. RabbitMQ/MassTransit health

- AMQP dial to `192.168.1.190:5672` vhost `shopixy` (user `shopixy-app`): OK.
  Management plugin `:15672` is not enabled (404 on all paths) — same as found; AMQP + bus
  health used instead. No harness publishes; TestAuth/debug/outbox routes never touched.
- API `/health`: `masstransit-bus: Healthy` with ALL loadtest-relevant endpoints `ready`
  (`product-created/updated-event`, `product-visibility-lifecycle-projection`,
  `product-cache-invalidation`, `redis-invalidation`, `product-search-index`,
  `product-updated-search-index`, `product-deleted-search-index`,
  `product-stock-price-search-index`, `product-stock-projection`,
  `storefront-config-projection-v2`, `shipping/navigation/footer/cms-page` projections, …).
- End-to-end delivery proven by §7 (5 s Mongo convergence on genuine lifecycle events) and
  §6 (ES sync indexing). `rabbitmq: Healthy` dependency check likewise green.

## 15. Prometheus/Grafana readiness

- TCP probes `192.168.1.190:9090/3000/9100/9093/8080` and `localhost:9090/3000`: all refused.
  API `/metrics` → 400 (no Prometheus scrape endpoint at that path in this build).
  No exporters attached this phase — IDENTICAL posture to M4.2A §16/§19 (client-observed
  verification only: API/Mongo/ES assertions, no capacity numbers, no subsystem attribution).
- K8s/perf-env telemetry remains the prerequisite before any future capacity claim.
  Not a blocker for rebuild verification; recorded as Limitation 3.

## 16. Manifest comparison

- `tests/load/data/manifests/latest.json` vs pre-M5 backup (`latest.json.pre-m5-backup`,
  kept in-repo as evidence): **byte-identical before freeze** (empty diff).
- Freeze (Phase 7, after all-green verification) updated ONLY metadata: `environment`
  (`local` → `loadtest-new`), `database` (`192.168.1.5/PosEcommerceDb_Baseline` →
  `192.168.1.190/PosEcommerceDb_LoadTest`), `verifiedAtUtc` (new timestamp), plus new
  `m5NewEnvironment` descriptor block; snapshot `m5-newenv-<stamp>.json` written.
  Changed top-level keys: `verifiedAtUtc`, `m5NewEnvironment`, `database`, `environment` — nothing else.
- Identity assertion (fail-closed script): tenant IDs/slugs, all 60 product
  (slug/productId/kind/SKU), all 66 variant (variantId/SKU), all 12 category, all stock-budget
  (variantId/warehouseId/quantity) rows and all 3 probe records — IDENTICAL. Seed version
  `m2.1`, namespace unchanged. All 7 k6 setup flags remain `true`.
- Known skews carried over unchanged (NOT introduced here): (i) variant `-D` SKU/barcode
  suffix in manifest vs bare SKU in SQL — proven identical on BOTH environments (§3);
  (ii) `commercehub_products_loadtest[_v2]` 6 pre-existing docs (non-loadtest, untouched).

## 17. Differences from old environment

- Postgres: `192.168.1.5/PosEcommerceDb_Baseline` → `192.168.1.190/PosEcommerceDb_LoadTest`
  (new approved target; old never written this phase).
- Mongo: `ecommerce_read_db` → `ecommerce_read_db_loadtest` (0 products/categories pre-rebuild
  → 20/4 per tenant, §5).
- OpenSearch: `http://192.168.1.5:9200` (`products`/`categories`) →
  `https://192.168.1.190:9200` (`products_loadtest` + shared tenant-filtered `categories`/
  `cmspages`; `commercehub_products_loadtest` alias → `_v2`; Basic Auth + cert pinning).
  Product SKU field is `sKU` (verifier uses doc-detected names, as in M3-B).
- Redis: `192.168.1.5:6379` standalone master, no password (`pos`) → `192.168.1.190:6379`
  HA pool (master + 2 replicas behind one endpoint), password + DB0 (`pos-loadtest`) —
  **with the replica fan-out defect and latency-comparability caveat (§13).**
- RabbitMQ: `192.168.1.5` `/` → `192.168.1.190` vhost `shopixy`, config `loadtest-windows`.
- Redis version: 6.0.16 → 7.0.15.
- UNCHANGED (comparability rule honored): tenant IDs, product IDs, sentinel identities,
  stock budgets, tenant/store relationships, workload routes, k6 scenarios, VU-to-tenant
  mapping (`lib/manifest.js` cycling), correctness assertions, isolation assertions,
  negative probes, request/correlation headers, k6 tags, threshold semantics. k6 files untouched
  (`git status` shows no modification under `tests/load/k6/`).

## 18. Blockers

- No hard blockers remain for the READ replay goal. The Redis replica fan-out (§13) is
  classified as **Limitation 1**, not a rebuild blocker, because every required end-state
  it touches is verified achieved through other evidence (Mongo 20/4 green despite the sync
  500; reads 200 fail-open; organic path 200 end-to-end). It MUST be fixed K8s-side before:
  (a) any future tenant re-sync is expected to return 200, (b) any latency/throughput number
  from this env is compared against M4.2A, (c) any write-heavy phase (e.g. M4.2-B
  transactional — explicitly NOT STARTED and not started from these results).
- First M5 executor attempt (full `m5_execute.py` run) was ABORTED after per-tenant
  product-sync 500s + spot-poll timeouts; it wrote NO report and froze NOTHING
  (`latest.json` still byte-identical at that point). All evidence in §§4–8 comes from the
  subsequent targeted re-runs documented above (same routes, same gates, sequential).
  The attempt's only persistent side effects: 3 owner-hash rotations (superseded by the
  final random rotation, §4) and completed Mongo upserts (desired end-state, §5).
- Files changed/added this phase (all under `tests/load/`; no app/business/schema/migration
  files touched): modified `python/orchestrate.py` (additive `m5_preflight` + constants;
  `m3b_preflight` untouched), new `python/m5_execute.py` (M3-B-equivalent runner retargeted
  to new infra), new `M5_NEW_ENVIRONMENT_REBUILD_REPORT.md` (this file), manifests
  `latest.json` (metadata-only freeze) + `m5-newenv-<stamp>.json` snapshot +
  `latest.json.pre-m5-backup` (pre-freeze evidence). Incidental `__pycache__` pyc restored.

## 19. Readiness for repeating M4.0/M4.1/M4.2-A + Final verdict

- The frozen k6 scenarios (`m4-deterministic-read.js`, `m4-concurrent-read.js`,
  `m4-local-capacity.js`, `lib/*`) replay UNCHANGED: manifest is the sole identity source
  (IDs identical, 7 setup flags `true`, `verifiedAtUtc` fresh), deterministic VU→tenant
  cycling preserved, same allowlisted GETs, same I-1/I-2/negative-probe gates, same headers/tags.
- HARD STOP respected: no M4.0/M4.1/M4.2-A executed, no 50/100/250 VU run, no load of any kind
  (`tests/load/results/` contains only pre-existing M4 logs; no new artifacts).
- Pre-replay checklist for the operator: (1) remediate Redis fan-out K8s-side (§13) and
  re-run one tenant product-sync to confirm HTTP 200 (Mongo already correct; this only
  re-arms the status contract); (2) rotate the 3 owner passwords via the authorized
  field-only procedure if authenticated phases are ever needed (k6 reads are anonymous);
  (3) attach Prometheus/Grafana before drawing any capacity conclusion (§15).
- **Final verdict: PASS WITH LIMITATIONS**
  - L1 (infra): Redis endpoint fans out to replicas — sync status 500 (data green), reads
    fail-open (correct but uncached: latency comparisons vs M4.2A need the §13 caveat).
  - L2 (pre-existing): manifest variant `-D` SKU/barcode suffix vs bare SQL values, identical
    on both environments; no workload impact (§§3, 16).
  - L3 (telemetry): no Prometheus/Grafana/exporters this phase; verification is
    API/Mongo/ES assertions only (§15).

## Appendix A. Request/correlation evidence

- All mutating/privileged calls went through `orchestrate.m3b_route_allowed` allowlist +
  `m5_preflight` (or the identical `seed.py` safety gate for SQL/rotation steps), per-tenant
  Owner JWTs (or anonymous + Host header for storefront reads), sequential tenants, unique
  `X-Request-Id == X-Correlation-Id` per call; secrets never logged or written to files.
- Call census (gated application-path calls actually executed):
  - `POST /api/auth/login` ×~12 (owner rotations' logins + per-tenant sessions; all 200).
  - `GET /api/admin/store-config` ×3+ (200, live, tenant-bound).
  - `POST /api/admin/sync/categories` ×3 (200, count 4).
  - `POST /api/admin/sync/product-variants` ×5 (500 ×5 — §13; Mongo upserts applied).
  - `POST /api/admin/sync/search/full` ×3 (200, 20+4, zero errors).
  - `POST /api/products/bulk/unpublish` ×7 (200 Processed ×7).
  - `POST /api/products/bulk/publish` ×7 (200 Processed ×7).
  - `GET /api/products/{id}` ×~20 (state reads around lifecycle transitions).
  - Anonymous storefront GETs ×~40 (9 paths × 3 tenants + 6 cross-tenant probes; §8/§11).
  - Direct PM read-only checks: Postgres verify ×2, Mongo counts/docs, OpenSearch
    `_doc/_count/_search`, Redis `ROLE/INFO/PING` (+ isolated client-behavior probes with
    `diag-*` keys only), RabbitMQ AMQP dial, `/health*` snapshots.
- First-attempt `m5_execute.py` evidence calls were superseded (run aborted pre-report);
  the per-call facts above are the authoritative record.

## Appendix B. Safety proof

- `seed.py` gates intact (env allowlist, `TEST_DB_PROOF` host+db match, prod-marker refusal,
  `LOADTEST_SEED_PASSWORD` strength, no `--force-production`); `orchestrate.m5_preflight`
  same strictness with NEW approved constants; `m3b_preflight` byte-logic untouched.
- No TestAuth, no global reindex/alias-promote/index-delete, no maintenance/debug/dev/outbox
  routes, no webhooks/providers/orders/payments, no checkout/cart/POS/sale writes, no direct
  Mongo/OpenSearch document writes, no SQL edits except the authorized 3-row
  `Users.PasswordHash` rotations (deterministic-Id + username + tenant triple-checked).
- Stock quantities never mutated (seed verify identical pre/post, §12). Test universe
  membership never mutated (catalog guard 20/20 throughout, §4). Manifest identities never
  mutated (byte-identical pre-freeze diff + metadata-only freeze assertion, §16).

# EasyOrders Public Technical Comparison vs Shopixy

Date of investigation: 2026-10-13 (UTC). All timestamps below are access times.
Classification legend used throughout: **Verified observation** (captured evidence or inspected source),
**Documented claim** (company-published), **Technical inference** (plausible, with basis and confidence),
**Unknown** (evidence unavailable).

No Shopixy source code or configuration was modified. No login, account, order, payment, fuzzing,
authenticated probing, or load testing of EasyOrders was performed. Public read-only access only.

---

## 1. Executive summary and key conclusions

- REA 6.4.0 CLI was verified end-to-end for browser observation against an isolated Edge instance:
  `list-browser-targets`, `inspect-web-page`, `analyze-web-bundle`, `capture-web-screenshot` all produced
  Evidence records. Codex MCP registration was applied (`ready` for the `codex` scope); the current
  OpenCode session does not load the new MCP server, so the CLI was used and no MCP-active claim is made.
- EasyOrders marketing site (`www.easyorders.eg`) is a statically generated **Astro** site with React
  islands (REA vendor fingerprint: React, medium confidence), Mixpanel + Google Ads instrumentation, and
  Cloudflare fronting (`cf-cache-status: DYNAMIC` on HTML).
- Merchant storefronts (`*.myeasyorders.com`) are a **Next.js** CSR application: HTML shell (~68 KB)
  served per-request (`Cache-Control: private, no-cache, no-store`) with `Server: easy-orders.net` behind
  Caddy; JS/CSS from `cdn.easy-orders.net/_next/static/...`, media from `files.easy-orders.net`
  (AVIF/WebP). Tenant identity resolves from subdomain to a `store_id` UUID cookie plus `theme_key` and
  `currency` cookies. Themes are Liquid/section-based with a palette system and an events reference —
  structurally similar to Shopify's theme model.
- Public API: REST at `https://api.easy-orders.net/v1`, Bearer-token auth, Products/Orders/Categories/
  Shipping endpoints, secret-header webhooks (`order created`, `order-status-update`), published rate
  limit of **40 requests/minute** with HTTP 429.
- Company-published scale figures (2026-10-13): 200,000+ registered stores, 300,000+ orders/day,
  10 countries, +15 payment gateways, +100 shipping companies, 23% of sales via AI. The implied average
  is ~1.5 orders/store/day — an average, not a peak/concurrent-throughput figure. Nothing about their
  database, tenancy internals, caching, or topology can be verified from public evidence.
- Shopixy (this repo, `PointOfSale.sln`) is architecturally the heavier system: serializable checkout
  transactions in PostgreSQL, MongoDB read-model projections with a SQL-authority overlay (also applied
  to Elasticsearch), generation-fenced Redis caches, MassTransit bus-outbox + Hangfire, and a certified
  tenant-isolation test freeze. Its storefront, however, is a pure CSR React SPA with no SSR, no output
  caching, and historically measured Mongo-connection-pool and full-document-read bottlenecks (Sept 2026
  load reports: pool-100 collapse at 6000 VU; pool-1000 diagnostic p50 2.6 s / p95 15.1 s).
- Highest-value Shopixy improvements emerging from the comparison: (1) SSR or prerendered shells for
  catalog/product routes; (2) CDN + cacheable immutable frontend assets with per-request HTML kept
  private (same split EasyOrders uses); (3) projected/lean Mongo reads and verified product-collection
  indexes; (4) storefront output/response caching with generation-fenced invalidation (pattern already
  proven in `RedisJsonCacheService`); (5) storefront search latency budget vs SQL-overlay cost; (6) cart
  hydration and image-format discipline (AVIF/WebP, lazy, explicit sizes).

---

## 2. Environment, REA setup status, versions, verified commands

### 2.1 Environment details (Verified observation)

| Item | Value |
|---|---|
| Working directory | `C:\Users\fadya\source\repos\PointOfSale` |
| Git branch / commit | `main` / `2563396` (short SHA) |
| Working tree | Pre-existing modifications present (shipping/geography feature work); nothing touched by this investigation (see §2.4) |
| Node.js / npm | `v22.20.0` / `10.9.3` (meets REA requirement: Node 22.x ≥ 22.19) |
| Edge | `152.0.4191.66` (`C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`) |
| Disk (C:) | Used ~218.8 GB, free ~6.7 GB |
| REA package | `rea-agents@6.4.0` via `npx` (matches npm `latest` checkpoint 6.4.0 published from `a56f7bb6` per installation doc) |

### 2.2 Documentation consulted (Verified observation)

- `https://github.com/morluto/rea` (README: `npx rea-agents setup`, Node ≥ 22.19 requirement, browser-analysis row).
- `https://github.com/morluto/rea/blob/main/docs/installation.md` (setup/doctor semantics, `--client` values incl. `codex`, dry-run/JSON modes, released-package boundary).
- `https://github.com/morluto/rea/blob/main/docs/browser-observation.md` — **unavailable**: HTTP 503 on fetch
  (raw GitHub path for that filename did not resolve). Mitigation: exact CLI syntax was taken from current
  `--help` output before each operation instead of memory.

### 2.3 REA setup for Codex (Verified observation)

- Dry-run reviewed first: `npx rea-agents@latest setup --client codex --dry-run --json` →
  `status: planned` with two actions (Codex MCP registration at
  `C:\Users\fadya\.codex\config.toml` with backup, plus shared skill install).
- Applied: `npx rea-agents@latest setup --client codex --yes --json` → `status: ready`,
  `appliedActions: [configured_codex, installed_skill]`; scoped doctor reports skill
  `installed_version 37`, `installed_tool_count 170`, `state: aligned`; Codex registration `aligned`.
- No global install was performed (`npx` runner only); no Hopper/Ghidra/IDA installed — correctly skipped,
  since browser/static-JS analysis needs no native engine. `doctor.environment_healthy: false` is
  expected and solely attributable to unconfigured native providers (GHIDRA_INSTALL_DIR unset, IDA MCP unset).
- **Limitation explicitly reported:** the current session runs in OpenCode, not Codex; the newly registered
  Codex MCP server cannot load here without a client restart/reconnect. All browser evidence below was
  therefore collected with the **REA CLI**, not MCP. MCP is registered but **not** claimed as active.

### 2.4 Commands successfully verified (Verified observation)

| Command | Result |
|---|---|
| `rea --version` | `6.4.0` |
| `rea setup --client codex --dry-run --json` | `planned`, 2 proposed actions reviewed |
| `rea setup --client codex --yes --json` | `ready`, Codex `configured`, skill `aligned` |
| `rea doctor --client codex --json` | scoped `healthy: true` (codex scope); global env unhealthy only for native providers |
| `rea list-browser-targets <endpoint> --json` | 1 marketing + 2 storefront page targets listed |
| `rea inspect-web-page <endpoint> <targetId> --observation-ms 3000 --include-json-body-shapes --include-storage-keys --json` | full page Evidence (21 scripts, 33 resources, storage keys) |
| `rea analyze-web-bundle <endpoint> <targetId> --observation-ms 3000 --json` | 21/21 scripts analyzed, import edges, React fingerprints, endpoint candidates |
| `rea capture-web-screenshot <endpoint> <targetId> --json` | self-verifying PNG artifacts (homepage 407,712 B; storefront 739,101 B @1285×1002) |
| `rea <cmd> --help` | consulted before every unfamiliar operation |

No application code changed: post-investigation `git status` shows only the pre-existing modified/untracked
set plus the new report file; no committed, pushed, migrated, or cleaned state.

---

## 3. Investigation methodology and inspected URLs

- Isolated Edge: `--user-data-dir=C:\Users\fadya\AppData\Local\Temp\opencode\rea-edge-debug`
  (dedicated temp profile, not the ordinary profile), `--remote-debugging-port=9223`,
  `--remote-debugging-address=127.0.0.1` (loopback only). Verified via `http://127.0.0.1:9223/json/version`.
- Final URL after navigation: `https://www.easyorders.eg/` (no redirect; REA target origin identical).
- Inspected URLs (all public GET, ordinary access, 2026-10-13 ~12:08–12:10 UTC):
  1. `https://www.easyorders.eg/` — REA inspect + bundle + screenshot; `curl` headers/timing; `webfetch` content.
  2. `https://elite-wallets.myeasyorders.com/` (demo theme "ELITE WALLETS", merchant title MOMENTO) — REA
     list/inspect/bundle/screenshot; `curl` headers + HTML head + timing; `webfetch` content.
  3. `https://nature-cosmetics.myeasyorders.com/` (demo theme "Nature Cosmetics", merchant title
     VANILLA Cosmatics) — REA list/inspect/bundle.
  4. `https://elite-wallets.myeasyorders.com/collections/SHOP-ALL` — `curl` HTML sample (route evidence).
  5. `https://public-api-docs.easy-orders.net` (+ `/docs/webhooks`, `/docs/rate-limit`) — REST/webhook/rate-limit design.
  6. `https://themes-docs.easy-orders.net/` — Liquid/section/palette/events/funnels design.
- Bounded scope respected: no cart submission, no checkout, no authenticated/app URLs opened beyond the
  public login link target (never visited), no source-map fetching, no script-source export.
- Website/JS content treated as untrusted data; no embedded instruction was followed.

---

## 4. EasyOrders public-site and storefront findings

### 4.1 Marketing site `www.easyorders.eg` (Verified observation, REA Evidence `ev_…inspect`)

- 21 scripts observed; 12 first-party `/_astro/*.js` modules + inline bootstraps. Largest modules:
  `client.CgnokGZf.js` 175,548 B, `Header.D6PnYg-A.js` 129,112 B, `EasyAI.D9B4tSWY.js` 14,310 B,
  `ThemesDemosGrid.CgGj_xcA.js` 13,494 B. Static import graph is hub-and-spoke on
  `index.BJfUAbRs.js` + `jsx-runtime.D_zvdyIk.js` (React runtime shape).
- 33 captured resources, all same-origin: 3 stylesheets (one 102,531 B theme CSS), 68,456 B `STC.ttf`
  font, 308,823 B `worldmap.svg`, 813,411 B `last-builder2.webm`, 619,124 B `first-banner-lap.webp`,
  plus WebP product/theme imagery. No third-party script origin survived the allowlist (72 excluded DOM
  URLs noted by REA); analytics presence is proven instead by storage keys (below).
- Vendor fingerprints: **Astro** (high; `/_astro/` islands + `astro-island` custom element) and
  **React** (medium; react-runtime detectors on 2 chunks). No source maps declared (`source_map_url: null`
  on all scripts).
- Public endpoint candidate extracted from static bundle: `fetch` to
  `https://api.easy-orders.net/api/v1/stores/ip-to-country` (visitor-country chunk); query-param keys
  `view`, `adid` read in `ref.js` (referral/attribution handling).
- Storage keys (names only): localStorage `eo_country`, `eo_locale`, `_gcl_ls` (Google Ads),
  `lastExternalReferrer*`; session + IndexedDB `mixpanelFlagsDb` (Mixpanel). No values retained.
- Headers: `Server: cloudflare`, `cf-cache-status: DYNAMIC`, HSTS preload, `via: 1.1 Caddy`.
- Single-sample timing (NOT a benchmark): HTML 459,790 B in ~0.41 s from this client.

### 4.2 Demo storefronts (Verified observation with a documented REA limitation)

- `elite-wallets.myeasyorders.com` → merchant storefront titled MOMENTO; `nature-cosmetics…` → VANILLA
  Cosmatics. Eight linked demo themes share the `*.myeasyorders.com` wildcard-subdomain pattern
  (elite-wallets, nature-cosmetics, nature-kids, blast-pets, hype-fashion, nature-pets, nature-handmade,
  phoenix-avyro) — **subdomain-per-store tenancy signal** (Technical inference, high confidence, corroborated
  by per-store `store_id` cookie below).
- Stack fingerprint: **Next.js** — `/_next/static/chunks/*` (webpack/framework/main/pages/_app),
  `data-n-g`, `next-head-count`, `data-optimized-fonts`. JS/CSS served from `cdn.easy-orders.net`,
  media from `files.easy-orders.net` (AVIF icon, WebP imagery), fonts from Google Fonts (Jost + Cairo).
- Storefront HTML (~68.5 KB, single sample ~0.74 s) is a CSR shell: `webfetch` (no JS) renders nav,
  collections (`/collections/SHOP-ALL`, `/collections/waist-bas`, …), `/pages/shipping-policy`,
  `/pages/refund-policy`, `/search`, then repeating `Loading...` placeholders. Collection HTML confirms
  per-route SSR/SSG titles (`<title>SHOP ALL</title>`) with the same shell.
- Response headers (storefront HTML): `Server: easy-orders.net`, `X-Powered-By: easy-orders.net`,
  `Via: 1.1 Caddy`, `Cache-Control: private, no-cache, no-store, max-age=0, must-revalidate`
  (per-request HTML, not edge-cacheable), `Set-Cookie: currency=EGP`, `store_id=<uuid>`,
  `theme_key=elite`, `Content-Security-Policy: frame-ancestors 'none'`, `X-Frame-Options: DENY`.
- REA limitation encountered and diagnosed: `inspect-web-page`/`analyze-web-bundle` on both storefront
  targets returned near-empty results (0 resources, 1 artifact: `captcha_bg.wasm` 91,415 B, no
  inferences). Cause: REA observation starts at attach, so the already-loaded bundle is missed, and the
  hydrated SPA exposes little statically; the WASM artifact suggests a bot/captcha challenge component on
  storefronts. Mitigation applied: structure/stack evidence was instead taken from `curl` HTML
  (Next.js markers, CDN URLs, headers) and a REA screenshot proving full client-side render
  (739,101 B PNG). The REA tools themselves remain verified-working (marketing page yielded full Evidence).
- Marketing vs storefront difference: static Astro marketing content vs per-request Next.js storefront
  shells; shared `files.easy-orders.net` media CDN; marketing behind Cloudflare, storefront HTML served
  dynamically (`DYNAMIC`-equivalent `private, no-cache`).

### 4.3 Public API, webhooks, themes (Documented claim + Verified observation of docs)

- Base URL `https://api.easy-orders.net/v1` (example: `GET /v1/products` with `Authorization: Bearer
  YOUR_API_KEY`). Surface: Products, Categories, Orders (get-by-id, notes, shipping update), webhooks,
  plus docs for authentication, permissions, query parameters, authorized-app links, and dropshipping
  provider connection (incl. Taager codes in webhook payloads).
- Webhooks: dashboard-created, per-webhook `secret` header, `order created` full-order POST
  (UUID `store_id`, COD fields, line items with variant/variation props, SKU/taager codes) and
  `order-status-update` events; deletion via `DELETE /api/v1/external-apps/webhooks/delete-by-url?url=`.
- Rate limit: **40 requests/minute**, HTTP `429 Too Many Requests`, custom limits via `info@easy-orders.net`.
- Themes: **Liquid** templates (`product.liquid`, `money` filter, `data-action="add-to-cart"`),
  section system (home/product/layout/utility), palette (colors/fonts without code), events reference for
  analytics hooks, and a Funnels builder accepting custom HTML (landing/checkout flows).

---

## 5. Verified company-published scale claims and their limits

Source URLs + date accessed 2026-10-13 (~12:09 UTC). All are **Documented claims**, not independent facts.

| Claim | Source | Figure |
|---|---|---|
| Registered stores | `https://www.easyorders.eg/` success section | 200,000+ |
| Orders per day (all stores) | same | +300,000/day |
| Sales through AI | same | 23% |
| Supported countries | same (+ regional sites `easyorders.ai`, `ma.`, `dz.`) | 10 |
| Payment gateways | same + footer | +15 |
| Shipping/delivery companies | same (worldmap section) | +100 |
| Pricing model | same `#Pricing` | $100/mo unlimited, or $0.04/order; store stays open if lapsed |

- Derived average: 300,000 ÷ 200,000 ≈ **1.5 orders/store/day**. This is a crude mean over registered
  (not active) stores; it says nothing about peak traffic, concurrent users, requests/second, checkout
  conversion under load, or demonstrated throughput. No latency, uptime, or capacity figure is published
  on the inspected pages.
- Explicit non-inferences: database technology, hosting topology, cache architecture, tenant isolation
  mechanism, autoscaling, deploy practices, and internal queue/worker design **cannot** be determined from
  the above and are classified **Unknown**. Single-sample timings in §4 are anecdotal, not benchmarks.

---

## 6. Comparison table (EasyOrders vs Shopixy)

| # | Dimension | EasyOrders (public evidence) | Shopixy (source/reports) | Classification |
|---|---|---|---|---|
| 1 | Public storefront structure & frontend assets | Astro SSG marketing + React islands; Next.js CSR storefront shells; immutable CDN chunks (`cdn.easy-orders.net`), media CDN (`files.easy-orders.net`, AVIF/WebP), Google Fonts | CSR-only React SPA (`PointOfSale.Storefront/src`, `createBrowserRouter`, no SSR); Vite build without manualChunks; lazy images but no AVIF/PWA/image pipeline; served via API `UseStaticFiles`, no CDN config found | Verified observation (both) |
| 2 | Observable navigation & request behavior | Collection/product/page/search routes; per-request HTML (`private, no-cache`); `store_id`+`theme_key`+`currency` cookies; `ip-to-country` geo lookup; referral params `view`/`adid` | Route-per-page SPA (`/ar/ /en/`, product/category/cart/checkout/tracking/search/CMS); tenant via `X-Tenant-Id` header discipline + host/slug resolution (`TenantResolutionMiddleware`, `HostResolutionMiddleware`); forwarded-headers support for proxy/CDN | Verified observation (both) |
| 3 | Rendering, delivery, client perf indicators | Static marketing HTML (~460 KB w/ heavy hero media: 813 KB webm, 619 KB webp, 309 KB svg); light storefront shell (~68 KB) + CDN chunks; single-sample 0.41 s / 0.74 s (anecdotal) | No SSR/output cache; full-doc Mongo finds per product call; identity-only 30 s/5 s memory cache (B1 adapt); historical 6000-VU diagnostics p50 2.6 s/p95 15.1 s (pool-1000) and pool-100 collapse | Verified observation; load numbers are historical report facts, not re-verified |
| 4 | Public API & webhook design | REST v1, Bearer auth, products/orders/categories/shipping, secret-header webhooks, 40 req/min + 429, permissions + app-link docs | Backend controllers exist for products/cart/checkout/payments/webhooks (Paymob/Fawry/generic) but no public third-party developer portal/rate-limit docs found in repo | EO: documented claim + observed docs; Shopixy: verified observation; gap is inference (medium) |
| 5 | Multi-tenant product architecture | Wildcard subdomains + `store_id` cookie + `theme_key`; per-store themes/media; internals unknown | Header strip→resolve→re-add, Active/not-deleted guards, 403 on mismatch, per-query `TenantId` predicates, Redis generation-fenced domain cache, frozen certification tests (`TenantIsolationCertificationFreezeTests.cs`) | EO externals verified; EO internals unknown; Shopixy verified |
| 6 | DB read/write, cache, background, search | Unknown (all server-side). Only client-visible: per-request HTML (no edge HTML cache), CDN static assets | PG serializable checkout (`CheckoutService.cs:6926`, `CommerceHubCheckoutService.cs:401,1301`); Mongo read models + projection writers + duplicate-key-tolerant consumers; Redis generation-fenced JSON cache + distributed locks; MassTransit bus-outbox (ReadCommitted) + retries + Hangfire; ES behind `ProductSearchSqlAuthorityOverlay` SQL-truth recompose | EO: unknown; Shopixy: verified observation |
| 7 | Scalability evidence & inference limits | 200k stores / 300k orders/day claimed (~1.5 avg); 429 policy implies per-client throttling; no capacity/latency proof public | Real load-test history: 3000-VU canonical med 289 ms/p95 1,998 ms; 6000-VU pool-100 failure (4,182/6,000 interrupted) → pool-1000 diagnostic 10,142/10,142 sessions, p50 2.61 s/p95 15.1 s/p99 21.6 s, zero wait-queue errors; A1 tenant-cache rejected (stale-auth); freshness-guard NO-GO pending | EO: documented claim; Shopixy: historical measured facts |
| 8 | Reliability, bottlenecks, perf-test methodology | Unknown internals; observable discipline: immutable CDN assets, light shells, documented 429s | Known knees: Mongo pool sizing + full-doc reads + `store-config`/search read timeouts; checkout serializable contention; no proven cart-cascade after pool fix; methodology: k6-style VU runs with session/request/p-latency accounting in `docs/reports/M4.5_*` | EO: unknown; Shopixy: historical verified |

---

## 7. Evidence references, paths, URLs, timestamps, confidence

- REA `list-browser-targets` 2026-10-13 ~12:08 UTC: 1 marketing target
  (`https://www.easyorders.eg/`, title Arabic) + 2 storefront targets (MOMENTO, VANILLA Cosmatics). High.
- REA `inspect-web-page` homepage: 21 scripts / 33 resources / storage keys / 72 excluded URLs. High.
- REA `analyze-web-bundle` homepage: 21/21 parsed, 110,760 AST nodes, React×2 (medium), endpoint
  `https://api.easy-orders.net/api/v1/stores/ip-to-country`. High (observation) / medium (vendor inference).
- REA `capture-web-screenshot` homepage + storefront: PNG sha256 `f262…3a1f` (407,712 B),
  `8396…e5cb` (739,101 B). High.
- Storefront HTML/head/headers/timing via `curl.exe` 12:10 UTC: Next.js markers, CDN hosts,
  `Server: easy-orders.net`, `store_id`/`theme_key`/`currency` cookies, `Cache-Control: private…`,
  68,505 B / 0.74 s single-sample. High (fact) / low (timing generality).
- Docs: public-api home/webhooks/rate-limit, themes home (full text captured 12:09–12:10 UTC). High.
- Shopixy paths (read-only, from repo): `PointOfSale.API/Controllers/Storefront*Controller.cs`,
  `Checkout*Controller.cs`, `Program.cs` (MassTransit/RabbitMQ/Hangfire/rate-limiting/middleware),
  `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs`,
  `PointOfSale.Infrastructure/{Data/ApplicationDbContext.cs, MongoDB/Services/{MongoDbContext.cs,ProductReadRepository.cs}, Redis/*, Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs, Accounting/AccountingOutboxProcessor.cs}`,
  `PointOfSale.Storefront/src/{app/router.tsx, vite.config.ts, services/api/*}`,
  `PointOfSale.Tests/Architecture/TenantIsolationCertificationFreezeTests.cs`,
  `MAIN_LOADTEST_REMEDIATION_PORT_REPORT_2026-09-25.md`,
  `MAIN_PRODUCTION_REMEDIATION_REVIEW_2026-09-25.md`,
  `MAIN_M4.5_MONGO_DEMAND_REMEDIATION_B1_REPORT_2026-09-25.md`,
  `docs/reports/M4.5_MONGO_{ROOT_CAUSE_DISCRIMINATION,HIGH_CEILING_POOL1000_DIAGNOSTIC}_2026-09-25.md`,
  `docs/reports/M4.5_TENANT_AUTHORITY_CACHE_A1_AB_VALIDATION_REPORT_2026-09-25.md`,
  `M4.5_PRODUCT_LIST_FRESHNESS_GUARD_REMEDIATION_REPORT_2026-09-27.md`. High for source facts;
  load numbers medium (historical, Sept 2026, not re-run).

---

## 8. Architectural questions that remain unanswered

1. EasyOrders database, tenancy isolation, caching, queue, and hosting internals (no public evidence).
2. EasyOrders checkout write path, inventory reservation, and payment orchestration internals.
3. Whether storefront HTML is SSR-per-request or edge-personalized; cache-hit ratios; real TTFB distribution.
4. Storefront bot/captcha (`captcha_bg.wasm`) trigger conditions and performance cost.
5. Public API versioning/deprecation, per-endpoint quotas, webhook retry/redelivery semantics.
6. Shopixy: global EF `HasQueryFilter(TenantId)` presence; per-tenant connection/options isolation.
7. Shopixy: product-collection Mongo indexes; exact catalog/list Redis TTLs; cart/session key design.
8. Shopixy: RabbitMQ topology/bindings; Hangfire recurring schedules; ES cluster/index/alias strategy.
9. Shopixy: current (post-Sept-reports) production latency — all load figures are historical.
10. Shopixy production static-hosting/CDN topology for the storefront bundle.

---

## 9. Prioritized Shopixy recommendations

**R1 — Add SSR/prerendered shells for catalog, product, and CMS routes.**
Expected impact: largest storefront LCP/TTFB win; parity with Next.js-shell + Astro-SSG pattern.
Evidence: EO shells ~68 KB vs Shopixy full CSR bootstrap (`Storefront/src/main.tsx`, `router.tsx`).
Area: `PointOfSale.Storefront/` (routes, data loading) + hosting in `Program.cs`/`deploy/`.
Effort: Large. Risks: tenant/theme personalization leaks into cached HTML; hydration mismatch (ar/en, RTL).
Validation: Lighthouse LCP + TTFB on product/category before/after; HTML must stay `private` where personalized.

**R2 — Put immutable frontend assets on a CDN with content-hashed filenames; keep HTML per-request.**
Impact: high (cache-hit static, small dynamic). Evidence: EO `cdn./files.` split; Shopixy `UseStaticFiles`
only, no CDN config. Area: `PointOfSale.Storefront/vite.config.ts` build + `deploy/` + cache headers.
Effort: medium. Risks: stale-bundle deploys; CORS/CSP (`img-src`, font) updates.
Validation: hashed-asset hit ratios, deploy-rollback test, CSP violation count = 0.

**R3 — Replace full-document Mongo product reads with projections/lean queries; verify product-collection indexes.**
Impact: high (directly attacks the historically proven knee: per-call full-doc finds, 6000-VU collapse).
Evidence: `ProductReadRepository.cs:169` full-doc `find`; `M4.5_MONGO_*` reports (pool-100 → 4,182
interruptions; pool-1000 → p95 15.1 s with residual `products`/`store-config`/search timeouts).
Area: `PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs`, index models, B1 identity cache.
Effort: medium. Risks: projection staleness (pair with freshness guard work); missed covering indexes.
Validation: bytes/read distributions, pool wait-queue = 0 at 6000 VU, p95 regression gate.

**R4 — Extend generation-fenced output caching to hot storefront reads (product, category, store-config).**
Impact: high under flash traffic. Evidence: pattern already proven in `RedisJsonCacheService`
(Lua-owned writes, `KeyedSingleFlight`, 45 s projection-lag bypass); EO serves static via CDN + dynamic HTML private.
Area: `PointOfSale.Infrastructure/Redis/*`, `StorefrontProductService.cs`, `StorefrontProductCacheInvalidator`.
Effort: medium. Risks: stale catalog/price/stock; invalidation gaps on projection lag.
Validation: hit rate, invalidation-lag SLO, chaos test (bump generation mid-flash-sale).

**R5 — Set and enforce a storefront search latency budget; measure SQL-overlay cost per query class.**
Impact: medium-high (search was a residual timeout class: 3 `search` timeouts in pool-1000 run).
Evidence: `ProductSearchSqlAuthorityOverlay` recompose-on-read + ES path.
Area: `Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`, `ProductDiscoveryService.cs`.
Effort: medium. Risks: relevance/freshness trade-offs if overlay is trimmed.
Validation: per-class p50/p95Overlay-ms dashboards; relevance holdout.

**R6 — Harden checkout serializable retries and idempotency keys; load-test payment-webhook storms.**
Impact: medium (correctness under contention). Evidence: serializable checkout transactions
(`CheckoutService.cs:6926`); Paymob/Fawry/generic webhook controllers; no storm test on record.
Area: checkout services, webhook controllers, outbox processors.
Effort: medium. Risks: duplicate charges on retry; advisory-lock contention.
Validation: duplicate-delivery test (same idempotency key ×N → 1 charge), serialization-retry rate SLO.

**R7 — Publish a versioned third-party developer surface (docs, Bearer scopes, per-client quotas, 429s, webhook retries).**
Impact: medium (ecosystem parity; EO documents all of these). Evidence: EO public-api/themes/funnels docs,
40 req/min policy; Shopixy has controllers but no developer portal found. Area: new docs + gateway policy.
Effort: medium. Risks: committing to compatibility; abuse without quotas.
Validation: docs build, quota/429 conformance tests, webhook redelivery contract tests.

**R8 — Adopt AVIF/WebP-first responsive images with explicit dimensions across storefront components.**
Impact: low-medium, cheap. Evidence: EO `files.easy-orders.net` AVIF/WebP; Shopixy `loading="lazy"` exists
but no format/size pipeline. Area: `Storefront/src/utils/resolveProductImage.ts`, image components.
Effort: small. Risks: older-browser fallbacks; CLS if sizes missing.
Validation: image bytes/page, CLS ≈ 0, fallback coverage.

**R9 — Re-run the Sept 2026 load battery against current MAIN before claiming any bottleneck fixed.**
Impact: methodological (prevents stale-diagnosis regressions). Evidence: all load figures are Sept 2026;
freshness-guard report is NO-GO pending gates. Area: `docs/reports/`, authorized perf environment only.
Effort: medium. Risks: none to prod if isolated env is used (mandatory).
Validation: 3000/6000-VU parity tables vs `M4.5_*` baselines.

---

## 10. Recommended follow-up tests (authorized local/load-test environments only)

1. SSR-shell spike: prerender top-20 product + 5 category routes; compare TTFB/LCP vs CSR baseline.
2. CDN split test: hashed assets via CDN, HTML `private`; verify zero stale-bundle incidents across 3 deploys.
3. Mongo projection test: lean product reads + index verification; 6000-VU run targeting wait-queue = 0 and p95 ≤ baseline 15.1 s.
4. Cache-invalidation chaos: generation bump under synthetic flash load; assert staleness window ≤ SLO.
5. Search budget test: per-class overlay timings; ES-only vs overlay-recomposed relevance/ freshness matrix.
6. Checkout contention: serializable retry rate under concurrent same-SKU stock races; idempotency-key duplicates.
7. Webhook storm: 10× duplicate deliveries from payment providers; assert exactly-once financial effect.
8. Tenant-isolation re-freeze: run `TenantIsolationCertificationFreezeTests` + cross-tenant fuzz in staging after any tenancy change.
9. Image diet: AVIF/WebP rollout A/B on bytes/page, LCP, CLS.
10. Full battery re-run: repeat `M4.5_*` 3000/6000-VU runs on current MAIN; publish deltas before any perf claim.

---

*End of report. Methods, limits, and classifications above; no EasyOrders private infrastructure is claimed known.*

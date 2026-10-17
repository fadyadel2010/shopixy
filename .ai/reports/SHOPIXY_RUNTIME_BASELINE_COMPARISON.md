# Shopixy Live Runtime Baseline vs EasyOrders

Date: 2026-10-13 (UTC); times are access times.
Legend: **Measured runtime** (browser-observed in this pass), **Source fact** (inspected file:line),
**Technical inference** (basis + confidence), **Unknown** (not measurable in this pass).
Prior reports: `EASYORDERS_PUBLIC_TECHNICAL_COMPARISON.md`,
`EASYORDERS_AUTHENTICATED_DASHBOARD_RECON.md`, `EASYORDERS_DASHBOARD_RUNTIME_FINDINGS.md`.

No application source or configuration was modified, no business record was created/edited/deleted,
no credentials/tokens/cookies/bodies/payloads/customer data were captured or persisted, and no
concurrency/stress/load test was run. All observations are single-client diagnostics, not benchmarks.

---

## 1. Environment and browser connection status (Measured runtime + Source fact)

| Item | Value |
|---|---|
| Backend | `http://localhost:5010` — `PointOfSale.API.exe` (PID 14320), `health` → 200. Launch profile `http` = Development (`PointOfSale.API/Properties/launchSettings.json:4-12`). No restart, no config change |
| Admin frontend | `http://localhost:5173/` → 200. **Development server** (Source fact: `vite.js` process PID 19760; served HTML contains `/@vite/client` + react-refresh inject). `vite.config.ts:55-76` (port 5173, `/api`+`/hubs` proxy → `localhost:5010`) |
| Storefront | `http://localhost:5174/` → 200 (Vite dev, PID 18604). Not exercised — out of scenario scope |
| Unrelated listeners | `:3000` WhatsApp Baileys bridge (PID 13040), `:5040`/`:7070` svchost/AnyDesk, `:9223` REA Edge. Untouched |
| Browser | Pre-existing isolated headed Edge session `opencode-ui-test` (in-memory profile) reused per workspace rules. EasyOrders CDP session (`eo-dashboard` @ 9223) never touched; dashboard target re-verified present afterwards (`296ADEF1`) |
| Auth | Pre-existing authenticated session in the isolated browser reused; **no credentials entered anywhere** (the provided QA credentials were never typed, pasted, or placed in commands/files). Account identity deliberately not inspected. Permission-gated screens (`/ecommerce/orders`, `/ecommerce/payment-methods`) were accessible |
| Tooling | `playwright-cli` `goto/snapshot/eval/select/requests/requests --clear/requests --filter` verified in-pass. `requests` yields method + sanitized URL + status (no timings/sizes/headers); durations/sizes came from the Performance resource-timing API (page-observable metadata only). Resource buffer caps at 250 entries in this browser — dev-module flood rotates out early API entries (limitation; worked around with `clearResourceTimings` + interaction-scoped reads) |

---

## 2. Screens and read-only interactions exercised

| # | Screen | Entry | Interactions (all read-only) |
|---|---|---|---|
| A | Dashboard `/` | Fresh `goto` ×2 (repeat for determinism) | 2 full loads; `تحديث` (refresh) button click; 10 s idle window |
| B1 | Orders `/ecommerce/orders` | Fresh `goto` ×2 | Initial load; status combobox → `Confirmed` → `Pending`; return to default via fresh `goto` (no record opened) |
| B2 | Sales `/sales-history` (fallback list) | Fresh `goto` | Filter-panel open (`تصفية`, no values entered); `التالي`/Next (page→0); `السابق`/Previous (page→1) |
| C | Products `/products` | Fresh `goto` | Initial load; page-2 probe (no pager rendered — single page of data) |
| D | Widgets | Dashboard `/` refresh | 7 widget endpoints re-fetched via `تحديث` |
| E1 | Settings `/settings` | Fresh `goto` | List load only; no field edited, nothing saved |
| E2 | Integrations `/ecommerce/payment-methods` | Fresh `goto` | Catalog + configured-list load only; no toggle/connect touched |
| — | Shipping `/admin/shipping/setup` | Landed once via unexplained transient redirect (§6.5) | Observed only; not exercised further |

Search boxes were never submitted; no sort-toggle on orders; no bulk actions, exports, or deletes touched.

---

## 3. Runtime request tables (sanitized; durations/sizes = single samples)

Conventions: `page`/`pageSize`, `sortBy`/`sortDirection`, flat `status=Value`. No `fields=` projection
observed on any list. All statuses below are 200 OK unless stated. Counts = API (non-static) requests.

### 3.1 Dashboard `/` — initial load (Measured runtime, ×2 identical loads)

24 requests / 12 unique — every endpoint below fired **exactly twice**, byte-identical order across both loads:

| Endpoint (sanitized) | Count | Status |
|---|---|---|
| `api/auth/session` | 2 | 200 (see §3.6 for the one-time 401→refresh dance on first contact) |
| `api/marketplace/incoming-requests` | 2 | 200 |
| `api/auth/verification-status` | 2 | 200 |
| `api/dashboard/stats` | 2 | 200 |
| `api/dashboard/sales-trend?period=week` | 2 | 200 |
| `api/dashboard/top-products?limit=5` | 2 | 200 |
| `api/dashboard/recent-sales?limit=8` | 2 | 200 |
| `api/dashboard/low-stock?limit=8` | 2 | 200 |
| `api/dashboard/category-sales` | 2 | 200 |
| `api/dashboard/payment-methods` | 2 | 200 |
| `api/settings` | 1 | 200 |
| `api/admin/store-config` | 2 | 200 |

Plus ~676 static requests (dev-mode unbundled modules — dev artifact, not a finding).
Navigation timing: `responseEnd` 5 ms, `domContentLoaded` 716 ms, `load` 723 ms (single sample, dev).
Paint entries unavailable (empty in this context). Idle 10 s after load: **0 new requests**.

### 3.2 Dashboard refresh (تحديث click) — 7 widget refetches, each ×1

| Endpoint | Duration | Transfer |
|---|---|---|
| `dashboard/stats` | 164 ms | 564 B |
| `dashboard/sales-trend?period=week` | 61 ms | 765 B |
| `dashboard/top-products?limit=5` | 70 ms | 302 B |
| `dashboard/recent-sales?limit=8` | 60 ms | 302 B |
| `dashboard/low-stock?limit=8` | 70 ms | 1,192 B |
| `dashboard/category-sales` | 70 ms | 302 B |
| `dashboard/payment-methods` | 124 ms | 302 B |

Inference (low-medium): the repeated 302 B bodies are a repeated empty-envelope size (sparse seed data);
not proof of emptiness — bodies were never read.

### 3.3 Orders `/ecommerce/orders` — 1 request per transition (Measured runtime)

| Interaction | Request (sanitized) | Count | Status |
|---|---|---|---|
| Initial load (+ identity fan-out: session×2, incoming×2, verification×2, settings×1) | `admin/orders?page=1&pageSize=20&sortBy=createdAt&sortDirection=desc` | 1 | 200 |
| Status → Confirmed (client-side URL transition to `?status=Confirmed&page=1`) | same + `status=Confirmed` | 1 | 200 |
| Status → Pending | same + `status=Pending` | 1 | 200 |
| Return to default (fresh `goto`) | unfiltered | 1 | 200 |

No duplicate, no shipping/config re-fetch per transition, no errors.

### 3.4 Sales `/sales-history` — mount ×2, interactions ×1, page=0 edge

| Interaction | Request | Count |
|---|---|---|
| Mount | `sales?page=1&pageSize=20` | 2 |
| Filter-panel open (no values) | +1 `sales?page=1…`, +1 `incoming-requests` | 1 each |
| Next (`التالي`) | `sales?page=0&pageSize=20` (115 ms, 365 B — empty) | 1 |
| Previous (`السابق`) | `sales?page=1&pageSize=20` | 1 |

### 3.5 Products, settings, payment-methods, shipping (mount counts)

| Screen | Notable requests (all 200) |
|---|---|
| `/products` | `products?sortBy=name&sortDirection=asc&page=1&pageSize=50` ×2; `categories` (no params — unbounded) ×2; settings ×1. 9 rows rendered, pageSize 50, no pager → page-2 untestable |
| `/settings` | `settings` ×3; `settings/default-country` ×2 (+ standard ×2 fan-out) |
| `/ecommerce/payment-methods` | `admin/payment-methods/catalog` ×2; `admin/payment-methods` ×2; settings ×1 |
| `/admin/shipping/setup` | `supported-carriers`, `carrier-accounts`, `warehouses`, `shipping/audit?limit=100` — each ×2 (+ fan-out) |

### 3.6 One-time auth dance (Measured runtime, first contact only)

Very first observation showed `api/auth/session` → 401 (×3 across early navigations) → `POST api/auth/refresh` 200 → `session` 200. Every later load: `session` 200 ×2 directly. Source mechanism: `AuthBootstrap.jsx:72-98` (401 → `requestPosSessionRefresh()`).

---

## 4. Duplicate-request analysis (Measured runtime + Source fact)

**Observed pattern:** mount/param-static loads duplicate (×2, deterministic across repeats and screens);
user-driven refetches single (×1). `settings` is ×1 on most screens but ×3 on `/settings`.

| Case | Evidence | Likely source (Source fact) | Confidence |
|---|---|---|---|
| Widget/sales/products/settings-page ×2 on mount | §3.1, §3.4, §3.5; repeat identical | Plain `useEffect` + axios mount effects double under React StrictMode **dev** (`main.jsx:8`): `PosDashboardContent.jsx:43-112`, `SalesHistory.jsx:25-27`, `Products.jsx:181-192,415-421`, `Settings.jsx:115-117` | High (mechanism), but **dev-only** — prod unverified |
| `session` ×2 every mount | §3.1–§3.5 | Intentional: cleanup releases the guard "so the replacement effect can perform the authoritative session bootstrap" (`AuthBootstrap.jsx:166-184`, comments explicit) | High |
| `settings` ×1 normally, ×3 on `/settings` | §3.1 vs §3.5 | Global `SettingsContext` effect is dep-gated (`[hasHydrated,isAuthenticated]`, `SettingsContext.jsx:95-107`) → fires once post-hydration; page effect has static deps (`Settings.jsx:115-117`) → StrictMode-doubled (1 + 2 = 3). Same split explains `default-country` ×2 | High |
| Orders list ×1 always | §3.3 | React Query `useQuery` with shared client (`staleTime` 30 s, `queryClient.js:3-11`) dedupes remount (`Orders.jsx:1458-1462`) | High |
| Interaction refetch ×1 | §3.2, §3.3, §3.4 | Same mechanisms: effects of post-mount state changes run once; React Query serves/dedupes | High |
| Sales search-submit may double | Code only, NOT runtime-tested | `handleSearch` calls `fetchSales()` directly AND `setPage(1)` triggers the fetch effect (`SalesHistory.jsx:25-27,50-54`) | Medium-low, needs measurement |

**Do not confuse** UI-request duplication with backend-query duplication: each doubled UI request is an
independent HTTP call (server executes twice); no evidence of a single request fanning out server-side.

---

## 5. Dashboard boot and widget-loading analysis

- **Boot fan-out per SPA navigation:** 4–6 identity/config round trips (`session`, `incoming-requests`, `verification-status`, `settings`/`store-config`) precede or accompany content; shipping screens add 4 more. Identity vs widget separation is clean (distinct endpoints), but everything mount-static doubles in dev.
- **Widget design (good):** 7 granular endpoints with tight limits (`top-products?limit=5`, `recent-sales?limit=8`, `low-stock?limit=8`), period-scoped trend (`period=week`), warehouse-scoped stats. Server side: `DashboardController.cs:18-19` caches aggregates **45 s** and recent-sales **15 s** via `IMemoryCache` (per-instance, Source fact) — a real short-TTL widget cache, though instance-local rather than shared.
- **Gaps:** no `fields=` projection on any widget/list response (full DTOs assumed — bodies never read, so byte waste is inference, medium); `settings` reads send explicit `Cache-Control: no-store` (`SettingsContext.jsx:116-121`, `Settings.jsx:142-147`), opting out of HTTP caching for a rarely-changing payload; `categories` fetch is unbounded.

---

## 6. Comparison against the three EasyOrders reports

| Dimension | EasyOrders (earlier single-client pass) | Shopixy (this pass) | Verdict |
|---|---|---|---|
| Boot fan-out | ~7 identity/config round trips per full document load | 4–6 per SPA navigation (no document reload between screens) | Different architectures; Shopixy SPA avoids EO's per-tab full reload. No winner declared |
| Orders initial load | Single 164 ms `limit=25` | Single `page=1&pageSize=20&sortBy=…` (React Query) | Parity on contract; Shopixy mount does not duplicate here |
| Filter transition | **2× list + 2× shipping re-fetch per click (production)** | **1× list, no collateral refetch** | Shopixy better; EO-F1 does not apply as a bug — it applies as a regression test to keep |
| Returned-to-default | 2× again | 1× | Same as above |
| Products list | Single 193 ms `limit=25` | 2× `pageSize=50` + 2× unbounded `categories` | EO leaner on the wire — but Shopixy 2× is a dev artifact; `categories` unbounded is real |
| Widgets | `statics` host, `low-stock?limit=5&fields=…`, `id‖$in‖` batch, `limit=1` probes | 7 granular endpoints, tight limits, 45 s/15 s server cache; no `fields=`, no batch/id-probe seen | Each side has something to copy (EO §8 R3/R5) |
| Integrations list | `apps?limit=500` 1,679 ms (slowest) | `payment-methods/catalog` + list (sizes unmeasured); `shipping/audit?limit=100` bounded | No `limit=500` smell found in Shopixy admin; EO-F2 stands as a general audit, not a proven Shopixy bug |
| Idle network | 0 req / 10 s | 0 req / 10 s | Parity (EO-D6 satisfied) |
| Third-party per nav | GTM×2, FB PageView per hash-nav, TikTok, Mixpanel, Ads | None observed on admin screens (dev; CSP restricts `connect-src`) | Shopixy admin cleaner by observation; prod may differ — Unknown |
| Asset loading | 6.7 MB single chunk, 11 resources (production) | ~676 static module requests (dev server) | **Non-comparable modes.** EO-D1 (measure Shopixy prod chunking) still open |
| Timings generally | Single-client EO samples, different network/account/server | Single-client localhost dev samples | No faster/slower verdict possible or attempted |

Prior-recommendation status: EO-F1 (duplicate fetches) → disproven as a Shopixy orders bug, convert to regression guard;
EO-F4 (slow integration hosts) → no Shopixy evidence yet (catalog sizes unmeasured); EO-F5 (dedicated analytics path) →
partially already implemented (`DashboardController` cache — extend, don't build); EO-D2/D3 (projection) → still open;
EO-D6 (idle) → verified parity.

---

## 7. Source-verified explanations for significant differences

1. **Why Shopixy orders don't duplicate but dashboard does:** `Orders.jsx` uses React Query (`useQuery`, shared key, `staleTime` 30 s) while dashboard/sales/products use bare `useEffect` + `api.get`. Under dev StrictMode the former dedupes, the latter double-fires. (Source facts: `Orders.jsx:1458-1462`, `queryClient.js:3-11`, `PosDashboardContent.jsx:101-112`.)
2. **Why `settings` is ×3 only on `/settings`:** dep-gated global effect (§4) + static-dep page effect (`Settings.jsx:115-117`) + StrictMode.
3. **Why Next on an empty sales list requests `page=0`:** with zero rows the backend reports `totalPages=0`; Next computes `Math.min(0, p+1)=0` and stays enabled because `disabled={page===totalPages}` compares 1===0 (`SalesHistory.jsx:166-174,189-197`). Backend answers 200 with an empty envelope (365 B measured). Minor edge bug, empty-store only.
4. **Why the 401→refresh dance appeared once:** stale/absent session cookie on first contact; `AuthBootstrap` refreshes then proceeds (`AuthBootstrap.jsx:81-93`, `api.js:80`).
5. **Why no `fields=` anywhere:** Shopixy admin clients request full DTOs (`AdminPagedResult<AdminOrderListItemDto>` server side, `AdminOrderService.cs:76-257` — prior citation, unchanged); EO projects only its widgets. Projection remains an open optimization on both sides' list rows.

---

## 8. Ranked recommendations (not implemented)

**R1 — Re-run §3 traces against a production build before optimizing anything.**
Evidence: every 2× in this report comes from a StrictMode dev server; the one React-Query screen already singles.
Area: `PointOfSale.Web` build/preview serving. Expected impact: high (avoids wasted work; may close the duplication class entirely). Risk: none (measurement only). Validation: prod-served mount traces show 1× per endpoint; else promote to bug with this report as baseline.

**R2 — Clamp the sales pager at 1 and disable Next when `totalPages < 1`.**
Evidence: §3.4 + `SalesHistory.jsx:166-174,189-197`. Impact: low (empty datasets only; kills confusing `page=0` round trip). Risk: trivial. Validation: Next on empty list issues no request and stays on page 1.

**R3 — Bound the products-screen `categories` fetch (paging, `limit=`, or client cache).**
Evidence: unbounded `GET /categories` ×2 per mount (`Products.jsx:181-192`). Impact: medium (grows with catalog; fires on every mount in dev, once in prod). Risk: low. Validation: bounded query string or cached read in mount trace.

**R4 — Field-project admin list rows (products/sales/admin-orders).**
Evidence: no `fields=` observed; EO's widget projections show the pattern's value. Impact: medium (payload discipline at scale). Risk: breaking clients expecting full DTOs — version/gate carefully. Validation: per-screen response byte budgets at seeded volumes.

**R5 — Justify or remove `Cache-Control: no-store` on settings reads.**
Evidence: `SettingsContext.jsx:116-121`, `Settings.jsx:142-147`. Impact: low-medium (settings fetched on many screens). Risk: stale settings rendering — pair with the existing 30 s `staleTime` or explicit invalidation. Validation: fewer settings bytes/load with a freshness test.

**R6 — Evaluate moving dashboard aggregates from `IMemoryCache` to the shared Redis substrate for multi-instance correctness.**
Evidence: `DashboardController.cs:18-19,45-60` (45 s/15 s TTLs, per-instance). Impact: medium under scale-out; nil single-instance. Risk: staleness semantics + invalidation wiring. Validation: hit-rate + cross-instance consistency check.

**R7 — Standardize new screens on the React-Query pattern (`Orders.jsx`) and add a mount-trace guard (1 click = 1 request in dev).**
Evidence: §3.3 vs §3.1. Impact: medium (prevents recurrence of the EO-F1 class). Risk: migration churn if applied retroactively — guideline for new code first. Validation: dev mount trace per new screen.

**R8 — (Unchanged from prior reports) Measure production bundle chunking for `PointOfSale.Web`.**
Evidence: dev served ~676 static modules; prod chunking still never measured (EO's 6.7 MB single chunk is the cautionary tale). Validation: per-route transferred bytes + Lighthouse on admin lists.

---

## 9. Classification ledger

- **Measured runtime:** all of §3; idle parity; 401→refresh once; deterministic repeat of dashboard load; DCL/load/paint attempt.
- **Source fact:** every `file:line` citation in §4, §5, §7, §8 (all read-only; nothing modified).
- **Inference (with confidence):** 302 B/365 B ≈ empty envelopes (low-medium); dev-duplication vanishing in prod (medium — mechanism certain, prod unmeasured); no faster/slower platform verdict (certain — samples non-equivalent).
- **Unknown:** response/cache headers and load-time transfer sizes (tooling limitation); prod-mode request counts; sort-toggle and search-submit behavior; products page-2; `payment-methods/catalog` payload size; backend per-request timings (no log access under the no-config-change boundary); cause of the single `/ecommerce/orders`→shipping detour (§6.5 detail below); websocket/SSE use (none observed); storefront runtime (unmeasured).

---

## 10. What could not be measured and why

1. **Production-mode behavior** — only the dev server is running; starting a prod build/serving path would change the running environment (out of scope).
2. **Response sizes / cache headers on loads** — `requests` exposes status only; the Performance buffer (250 entries) rotates out early API entries under dev-module flood. Interaction-scoped reads (clear-then-act) recovered samples (§3.2, §3.4).
3. **Sort toggle, search submit, products page-2** — sort not exercised (time/risk tradeoff, no data impact either way); search submits deliberately avoided (per-keystroke fetch noise); product data has a single page.
4. **First-navigation detour** — one `goto /ecommerce/orders` landed on `/admin/shipping/setup` with that screen's fan-out; an immediate clean repeat stayed on orders with a single list fetch. No redirect source found (`PosPermissionRouteGuard` falls back to `/`, `Orders.jsx` has no shipping redirect). Treated as an unexplained transient, not a finding; flagged for re-observation rather than action.
5. **Backend timings/logs** — readable only by attaching to or reconfiguring running services; excluded by the scope boundary.
6. **Storefront + super-admin + marketplace screens** — outside the EO-comparison surface; untouched.

---

*Temp artifacts: none written inside the repo (all reads in-memory). Working tree untouched (only pre-existing
`.opencode-tmp` deletions observed via read-only `git status`). EO session undisturbed (target re-verified).
Stop: report delivered, recommendations not implemented.*

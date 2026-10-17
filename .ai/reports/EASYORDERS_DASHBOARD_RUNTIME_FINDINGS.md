# EasyOrders Dashboard Runtime Findings vs Shopixy

Date: 2026-10-13 (UTC); times are access times.
Legend: **Verified runtime** (browser-observed during interaction), **Static evidence**
(bundle/route strings), **Technical inference** (basis + confidence), **Unknown**.
Prior reports: `EASYORDERS_PUBLIC_TECHNICAL_COMPARISON.md`, `EASYORDERS_AUTHENTICATED_DASHBOARD_RECON.md`.
No bodies, screenshots, tokens, cookies, secrets, or customer/financial data were persisted. No business
record created/edited/deleted; no Shopixy file changed.

---

## 1. Method and tooling status (Verified runtime)

- Endpoint `http://127.0.0.1:9223` alive; dashboard target present throughout (Verified runtime).
- `playwright-cli -s=eo-dashboard attach --cdp=http://127.0.0.1:9223` verified working — the supported
  local control mechanism. Only read-only acts used: `tab-list/select/new/close`, `snapshot`, `eval`
  (DOM reads + ordinary button clicks), `reload`, `goto` (deep links). No form submits, no toggles.
- REA `observe-web-session` verified for driven navigation (40 s window, 9 timeline events, 0 dropped).
- Mechanism limits discovered (all Verified runtime): (a) `inspect-web-page` fails with `target_changed`
  if a full document reload occurs mid-window (×2 observed) — it cannot span reloads; (b) its `network`
  section was empty in every run, and `observe-web-session` timelines carry navigation/lifecycle but no
  per-request method/status/size — so API traces were taken from the Performance resource-timing API
  (page-observable metadata: host/path/query/duration/initiator; **no statuses, no cache headers, no bodies**).
- Test tabs opened for this pass were closed afterwards; original tabs (2 storefronts + dashboard) restored.

## 2. Screens observed

| Screen | Entry | Transition type | Interactions exercised |
|---|---|---|---|
| Orders `#/orders` | Deep-link new tab | Full document load | Initial load; status filter paid → all (ordinary clicks) |
| Orders sort control | Same tab | n/a | Sort button located but exact-label click missed (`SORT_NOT_FOUND`); toggle **not** exercised |
| Products `#/products` | Deep-link new tab | Full document load | Initial load; pagination probed — no page controls rendered (single-page list) |
| Dashboard root `#/` widgets | Existing tab boot trace | Full document load (earlier reload) | Read-only read of boot resource timings |
| Plugins `#/plugins` | Deep-link new tab | Full document load | List load only; no toggle touched |
| Search box | — | — | **Not exercised** (per-keystroke fetch risk); limitation |

REA Evidence: both filter clicks recorded as `same_document_navigation` (no document reload) with full
React-Admin URL state — `filter={"status":"paid"}`, then `filter={}`, `order=DESC&page=1&perPage=25&sort=created_at`.
Two `load_failed … ERR_ABORTED` events coincide with the transitions (URLs redacted by REA; consistent
with superseded in-flight fetches — inference, low-medium).

## 3. Verified request traces (sanitized; durations = single client samples, not benchmarks)

Base hosts proven at runtime: `api.easy-orders.net`, `shipping.swqly.net`, `statics.easy-orders.net`,
`apps.easy-orders.net` (all `/api/v1/…`).

**Boot (every fresh tab):** `plugins/store-plugins/is-active/moderators` (~140–200 ms) →
`users/me/store` (72–167) → `stores/settings` (71–165) → `notifications?limit=25&page=1&sort=created_at,DESC&offset=0`
(~160–220) → `currencies?limit=500` (~150) → `users/stores` (~160–190) → `users/me` (~145–180).
About seven identity/config round trips before first content on every full load (Verified runtime).

**Orders list:** initial `GET orders?limit=25&page=1&sort=created_at,DESC&offset=0` (164 ms, single).
Paid filter → `GET orders?filter=status||eq||paid&limit=25&page=1&sort=created_at,DESC&offset=0` fired
**twice** (313 + 315 ms); back to all → twice again (264 + 214 ms). Each transition additionally
re-requested `plugins/shipping/api-key` (~176–220) and `shipping.swqly.net …/tenant-shipping-settings`
(~172–216). Boot-time shipping-settings call took **1,168 ms** once. No `fields=` projection on list rows;
redundant `offset=0` alongside `page=1`.

**Products list:** single `GET products?limit=25&page=1&sort=created_at,DESC&offset=0` (193 ms). No page-2
control existed to click.

**Dashboard widgets (boot):** `orders/statics` (154) → KPI cards; `products/low-stock?limit=5&
fields=id,name,thumb,quantity` (160) → minimal projection; `events/analytics?from=2026-10-06&to=…-13`
on `statics.easy-orders.net` (141) → 7-day chart; `products?filter=id||$in||<uuid>,<uuid>` (138) →
ID-batch fetch; existence probes `products?page=1&limit=1` (127), `categories?page=1&limit=1` (134);
`pages/home` (182), `banners` on `apps.` host (65), `stores/transaction-need-to-send-to-pixel` (139).

**Plugins screen:** `apps.easy-orders.net …/apps?limit=500` (**1,679 ms**, unpaginated full catalog —
slowest observed) + `plugins/store-plugins/active` (200).

**Third-party per load/nav (hosts only):** googletagmanager (GTM-NVX9NBB4, G-Y1F28T2SY8), Facebook Pixel
(PageView re-fired on each hash nav), TikTok beacons, Mixpanel (`mpc-prod…run.app`), Google Ads (2
conversion IDs), `react-admin-telemetry.marmelab.com`, DigitalOcean Spaces image. Unknown: payload
bytes (transferSize mostly 0 = cached/opaque) and any PII in beacon payloads (not inspected).

## 4. Shopixy comparison (source-verified)

- **List paging/filtering — implemented:** `AdminOrderService.cs:76-257` (`PaginationGuard`, `Skip/Take`,
  Status/PaymentStatus filters, `AdminPagedResult<AdminOrderListItemDto>`), `AdminShipmentService.cs:64-152`.
  EO runtime confirms the same shape (`limit/page/sort/filter||eq`) reaches the wire — parity on contract.
- **Proven gaps:** (a) duplicate list fetches per filter change (2× orders + 2× shipping calls = ~6 requests
  per click) — Shopixy admin must be audited for the same React-Admin/StrictMode double-subscribe habit
  (`PointOfSale.Web/src/main.jsx:8` wraps `<StrictMode>`; dev-only double effects, but the EO case is
  production, so the likelier cause is dual dataProvider subscribers — Unknown, needs measurement);
  (b) unpaginated `apps?limit=500` (1.7 s) — any Shopixy `limit=500`-style admin fetch is the same smell;
  (c) no `fields=` projection on EO list rows — Shopixy list DTOs need the same byte-budget audit (D2/D3
  from prior report, now runtime-proven valuable).
- **Already-good patterns to copy:** dedicated `statics.` host for analytics (isolates KPI/chart load from
  transactional API — matches Shopixy Redis analytics TTL substrate); ID-batch fetch (`id||$in||…`) instead
  of N+1; `limit=1` existence probes; per-screen minimal widget projections.
- **Needs measurement:** Shopixy admin equivalents of `users/me+stores+settings+notifications` boot fan-out
  (count round trips per full load); checkout of `offset`+`page` redundancy in Shopixy clients; cache headers
  on both sides (unmeasurable in this pass on either side).

## 5. Prioritized recommendations (not implemented)

**F1 — Kill duplicate list fetches (orders filter fires 2× list + 2× shipping).**
Impact: high (halves list-load traffic). Evidence: §3 timings. Area: admin data layer
(`PointOfSale.Web` query hooks) + verify API idempotency under double-fire. Effort: small-medium.
Risk: masking a needed refetch (focus/optimistic update). Validation: 1 filter click = 1 list request in
staging capture; re-run EO-style transition test.

**F2 — Paginate or virtualize full-catalog admin fetches (`limit=500` pattern; EO 1,679 ms).**
Impact: medium-high. Area: any admin `limit≥100` reads + their services. Validation: p95 of slowest admin
read ≤ budget (e.g. 500 ms) at seeded 10k-row tables.

**F3 — Field-project all admin list rows; keep `low-stock`-style widget queries.**
Impact: medium. Evidence: EO projects widgets but not rows; Shopixy DTO shape unverified.
Validation: per-screen response byte budgets.

**F4 — Isolate slow integration hosts from boot path (EO shipping host 1,168 ms once; apps host 1.7 s).**
Impact: medium (tail latency). Area: Shopixy carrier/shipping admin reads (`ShippingQuoteService`,
carrier adapters) — defer/parallelize with timeouts. Validation: boot p99 with simulated slow carrier.

**F5 — Adopt a dedicated analytics read path for KPI/chart widgets.**
Impact: medium. Evidence: EO `statics.` host + `orders/statics`; Shopixy has the Redis substrate —
wire widgets to it instead of transactional reads. Validation: widget p95 + hit rate.

**F6 — Third-party audit: per-nav PageView + per-load pixel/beacon fan-out.**
Impact: low-medium (perf + privacy surface). Validation: allowlisted tracker inventory + bytes/nav budget.

## 6. Unknowns and limits

Statuses, cache headers, response sizes/envelopes (Performance API lacks them); sort-toggle behavior;
search behavior; pagination beyond 25 rows; page-size change effect; websocket/SSE use; auth token
mechanics; server-side tenancy/caching/queue internals (unchanged boundary). All durations are
single-sample client observations from one network — indicative only.

---

*Pages inspected: `#/orders` (load + paid/all filters), `#/products` (load), `#/` widgets (boot trace),
`#/plugins` (load). No mutations, no bodies/screenshots persisted, working tree preserved. Stop.*

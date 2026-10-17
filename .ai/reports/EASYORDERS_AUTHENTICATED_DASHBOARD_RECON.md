# EasyOrders Authenticated Dashboard Recon vs Shopixy

Date of investigation: 2026-10-13 (UTC). Times are access times.
Classification legend: **Verified observation** (captured evidence or inspected source),
**Documented claim** (company-published), **Technical inference** (basis + confidence stated),
**Unknown** (evidence unavailable).

Prior public findings: `.ai/reports/EASYORDERS_PUBLIC_TECHNICAL_COMPARISON.md` (2026-10-13).
This report extends it with dashboard-session evidence. No Shopixy code or configuration was changed,
no business record was created/edited/deleted/published/submitted, and no credentials, tokens, cookies,
secrets, or customer/financial payloads were read, exported, or persisted.

---

## 1. Connection and inspection status

| Item | Status |
|---|---|
| REA CLI version | `rea-agents@6.4.0` (`--version` re-verified this session) |
| Command syntax | Re-verified via `--help` before use (`list-browser-targets`, `inspect-web-page`, `analyze-web-bundle`) |
| Debug endpoint | `http://127.0.0.1:9223` reachable; `Edg/152.0.4191.66`, protocol 1.3 (Verified observation) |
| Dedicated profile | Same isolated Edge (`rea-edge-debug` temp user-data dir); ordinary profile untouched |
| Dashboard target | Present: `target_id 296ADEF16520010AE809A5A456C92C2F`, title `Easy Orders Dashboard \| …`, URL `https://app.easy-orders.net/?_gl=…#/` (Verified observation) |
| Auth state | Authenticated session supplied by the user; login flow never touched, no credentials requested |
| Screenshots of dashboard | Deliberately **not taken** (would risk persisting business/customer data) |
| Mutations | None. Observation windows only; no clicks, submits, or constructed API requests |

No blocker: the endpoint was available and the authenticated dashboard target was present, so inspection
proceeded. Interactive navigation of inner screens (orders list, product edit, settings) was **not**
driven; findings below come from passive REA Evidence on the live dashboard target.

---

## 2. Dashboard modules actually inspected

Only the dashboard root target (`app.easy-orders.net# /`, single frame, hash-router) was attached.
No inner route was navigated to. Module coverage therefore comes from the delivered bundle's static
API surface (197 endpoint candidates, 9/9 scripts parsed, 1,700,927 AST nodes) plus idle-window
observation — i.e. the modules below are **proven to exist in the dashboard client**, with per-screen
runtime behavior mostly unobserved:

Identity & stores: `/users/me`, phone-OTP send/verify, `users/stores` (create), email/password update,
`stores/transfer-ownership`, `stores/settings` (get+patch), `general-settings`, `currencies`, onboarding flags.
Catalog: `products` CRUD, `/products/low-stock?limit=5&fields=id,name,thumb,quantity`, `/products/bulk-update`,
import (`products?is_import=true`), product sheets, `theme-schema`, Google Merchant settings.
Orders: `orders`, `orders/statics`, `/order-notes`, `/orders/excel` (export), `/orders/pos` (create),
`/missed-orders/convert|export`, `/orders-popup`, `tenant-order-cites`, spam/blocked-phone controls.
Shipping: `tenant-shipping-settings`, `tenant-shipping-method-settings` (+`/fulfill`, `/cancel`, `/return`),
cities/countries settings, carrier plugins (jt-express, oto, sendit), `shipping-options`, cache invalidation.
Payments: `store-payment-methods`, Tamara capture, Kashier transactions + EGP exchange.
Marketing/growth: `/pixels`, `/funnels` (+ AI funnel generation `/ai/ai-funnels-generate`), `/newsletter`,
`/ab-tests`, `/downsell-rule`, upsell, reviews, social-links, thanks-page, `checkout-fields`, AI requests.
Messaging: WhatsApp register/send/bulk/templates, `/whatsapp-api/*`.
Integrations/plugins: active/toggle/all lifecycle, Taager/TajerCOD, Ecombo, Shopify (auth/products/settings),
WooCommerce, Angazny, Drope, Jamayka, Mosaweq, Alturky, sheets, apps registry.
Themes/storefront: theme list with `filter=is_custom_theme||eq||false&filter=is_active||eq||true`,
purchase, demos, fonts, `stores/clone-demo`, `pages/storefront-update-token`, `pages/home`, ui-blocks.
Platform: `events/analytics`, exports, transactions, digital products/delivery, FCM devices, `stores/refs`.

---

## 3. Verified browser and network findings (sanitized metadata only)

- **App shape:** single frame, hash-router (`#/`), title `Easy Orders Dashboard`. 1,607 DOM nodes
  (structure counted only; no text mined). Zero network requests and zero websocket connections in a
  10 s idle observation window — the dashboard does not visibly poll while idle (prior traffic is
  unavailable by REA design, so polling with period > 10 s cannot be excluded).
- **Bundle:** `assets/index-DcVy_JLK.js` **6,744,347 B** single chunk + `react-SIfiwpqq.js` (8,185 B) +
  `WhatsAppSupportButton` (537 B) + inline bootstraps; 11 same-origin resources totaling ~7.09 MB;
  **no source maps**, no code-splitting evidence. Inferences: Vite + React (medium each).
- **Framework signal:** storage keys `RaStore.theme`/`RaStore.version` indicate **React-Admin**
  (Technical inference, medium-high); alongside `user-accounts`, `onboarding-all-stores`, Google `_gcl_ls`,
  TikTok `tt_*` pixel keys, Mixpanel `mixpanelFlagsDb` (key names only, values redacted by REA).
- **Query conventions (static, high value):** `filter=status||eq||accepted&sort=created_at,desc`,
  `fields=…`, `limit=500`, `sort=position,asc`, `checkout-fields?sort=is_active,desc` — server-side
  filtering/sorting/field-projection discipline, plus minimal widget projections
  (`low-stock?limit=5&fields=id,name,thumb,quantity`). Pagination envelope shape itself was not observed.
- **API client grouping (inference, low-medium):** distinct client instances (`LM` core, `BM`, `l4`
  shipping/tenant, `zM` payments, `RM` pixels, `UM` funnels, `HM/VM` WhatsApp, `WM` analytics) suggest
  domain-grouped API clients with separate configuration.
- **External calls referenced statically:** `api.shaahbndr.com/auth/login`, `api.ecombo.network/*`
  (dropshipping), `cloudflare.com/cdn-cgi/trace`. Dashboard API base host is **not** proven by relative
  paths (likely `api.easy-orders.net`, consistent with public docs — Unknown).
- **Auth-adjacent:** OTP-via-phone endpoints and a `storefront-update-token` bridge (dashboard → storefront
  theme publishing) exist; token storage/mechanism not observed (Unknown).

---

## 4. Comparison with relevant Shopixy source code

- **Order listing/filtering:** Shopixy `PointOfSale.API/Services/Admin/AdminOrderService.cs:76-257` —
  tenant-scoped paged list (`Status`/`PaymentStatus` filters, `PaginationGuard`, `Skip/Take`,
  `AdminPagedResult<AdminOrderListItemDto>`, deterministic `CreatedAtUtc` ordering) mirrors EO's
  `orders` + `filter||eq||accepted` convention. Shipments follow the same pattern
  (`AdminShipmentService.cs:64-152`). Gap: EO additionally projects fields (`fields=`) and uses
  compact widget queries (`limit=5`) — Shopixy list DTOs should be checked for over-fetching.
- **Missed/lost orders:** EO `/missed-orders/*` + `/orders-popup` correspond to Shopixy
  `AdminAbandonedCheckoutsController.cs:68,287` (paged list + summary statistics) — functional parity,
  worth a UX/latency side-by-side in staging.
- **Analytics:** EO `orders/statics` + `events/analytics` vs Shopixy `AdminAnalyticsAlertsController`
  (`api/admin/analytics/alerts/…`) and Redis analytics TTLs (24 h events / 25 h counters / 7 d idempotency
  per prior audit) — Shopixy already has the caching substrate EO-style `statics` widgets would reuse.
- **Tenant-aware design:** Shopixy enforces tenancy server-side (`TenantResolutionMiddleware` header
  strip→resolve→re-add, per-query `TenantId`, frozen certification tests); EO client evidence shows
  store UUIDs in payloads/cookies but its server-side isolation is Unknown — no parity claim is made.
- **Webhooks/integrations:** Shopixy has provider webhook controllers (Paymob/Fawry/generic) and carrier
  adapters with trust-pipeline tests, but no dashboard-managed webhook UI with secret rotation was found —
  EO documents exactly that (create/secret/delete-by-URL). Likewise EO's plugin active/toggle lifecycle
  and per-carrier settings pages exceed what Shopixy's admin UI was verified to expose.
- **Dashboard bundle:** EO ships ~6.7 MB in one chunk; Shopixy merchant/admin UI lives in
  `PointOfSale.Web` (React, Vite) but its production chunking was not measured — Unknown, flagged as R1.

---

## 5. New insights unavailable from public storefront inspection

1. Dashboard is a Vite + React (likely React-Admin) monolith with a ~6.7 MB un-split initial chunk —
   a concrete first-load liability the public shell analysis could not reveal.
2. True integration breadth: dropshippers (Taager, Ecombo, Shaahbndr), importers (Shopify, WooCommerce),
   regional carriers (JT Express, Oto, Sendit), Tamara/Kashier payments, WhatsApp Business messaging,
   TikTok/Meta pixels, funnels + AI generation — far beyond the +15/+100 marketing counts.
3. Server-side list conventions (`filter||eq`, `sort=`, `fields=`, `limit=`) and minimal widget projections.
4. Idle dashboard issues zero observable requests in 10 s (no chatty polling baseline to beat).
5. Dashboard↔storefront publishing bridge (`storefront-update-token`, `clone-demo`, theme purchase).
6. Phone-OTP identity endpoints and store-ownership transfer exist as first-class APIs.

---

## 6. Remaining unknowns and evidence limitations

- Inner screens (order list/filter UX, product editor, settings, theme studio) never rendered in an
  observation window; their runtime requests, payload sizes, statuses, timings, and cache headers are Unknown.
- Dashboard API base host, auth token storage/refresh, pagination envelope, and websocket/SSE usage Unknown.
- Endpoint list mixes real routes with query-param/string constants and duplicate client aliases;
  each candidate needs runtime confirmation before being treated as a route.
- No load, concurrency, or backend-topology inference is possible from this evidence (same boundary as §5
  of the public report). Single idle window ≠ proof of no polling.
- REA attach-window limits: pre-attach traffic, console text, and cross-origin metadata unavailable by design.

---

## 7. Prioritized, evidence-backed recommendations (not implemented)

**D1 — Split the merchant/admin bundle by route; audit `PointOfSale.Web` chunking.**
Impact: high (EO's 6.7 MB single chunk is the cautionary example). Evidence: `index-DcVy_JLK.js` 6,744,347 B,
no `manualChunks` equivalent. Area: `PointOfSale.Web` build + serving path. Effort: medium.
Risks: shared-vendor duplication across chunks; RTL/ar-en splits. Validation: per-route transferred bytes,
Lighthouse on admin order/product lists.

**D2 — Standardize server-driven filter/sort/field-projection on all admin lists.**
Impact: medium-high (payload discipline). Evidence: EO `filter||eq||accepted`, `fields=`, `sort=`;
Shopixy `AdminOrderService`/`AdminShipmentService` page + filter but DTO projection unverified.
Area: `PointOfSale.API/Services/Admin/*`, `AdminPagedResult<>` DTOs. Effort: medium.
Risks: breaking admin clients expecting full DTOs. Validation: list-payload byte budgets per screen.

**D3 — Minimal-projection read models for dashboard widgets (low-stock pattern).**
Impact: medium (attacks full-document-read habit). Evidence: EO `low-stock?limit=5&fields=id,name,thumb,quantity`;
Shopixy `ProductReadRepository.cs:169` full-doc finds (prior audit). Effort: small-medium.
Risks: staleness (pair with freshness guards). Validation: widget query bytes + p95.

**D4 — Short-TTL cached `statics`-style widgets reusing analytics Redis substrate.**
Impact: medium. Evidence: EO `orders/statics`; Shopixy Redis analytics TTLs already exist.
Area: admin analytics services + `Redis/*` invalidation. Effort: small. Risks: stale KPIs.
Validation: hit rate + max-staleness SLO per widget.

**D5 — Dashboard-managed webhooks (create/secret rotation/delete) + per-plugin least-privilege clients.**
Impact: medium (ecosystem + security posture). Evidence: EO webhook + plugin toggle APIs; Shopixy
provider webhooks exist without a management UI. Effort: medium-large. Risks: secret sprawl; SSRF via
callback URLs (allowlist + validation required). Validation: rotation/redelivery conformance tests.

**D6 — Verify no chatty polling in Shopixy admin/storefront (EO idle baseline: 0 req/10 s).**
Impact: low-medium, cheap. Area: `PointOfSale.Web`, `PointOfSale.Storefront` runtime. Effort: small.
Validation: 60 s idle request capture per key screen = ~0 non-essential requests.

**D7 — Re-checkout phone-OTP/auth flows against Shopixy `CustomersAuthController` for parity gaps.**
Impact: low (feature intel only). Effort: small (read-only). Validation: gap list, no code change.

---

## Conclusion

- **Pages/capabilities inspected:** dashboard root target only (`app.easy-orders.net#/`, single frame);
  module inventory derived statically from its 6.7 MB Vite/React bundle (197 endpoint candidates across
  identity, catalog, orders, shipping, payments, marketing, messaging, integrations, themes, platform).
- **Blockers:** none for passive inspection; interactive inner-screen navigation and runtime API
  request capture were out of scope for this pass (would require driving the authenticated session or
  longer observation windows during active use).
- **Data safety:** metadata and key-names only; no bodies, screenshots, tokens, or customer/financial
  records persisted. No Shopixy file modified; working tree preserved; no business data touched.

*Stop: report produced, recommendations not implemented.*

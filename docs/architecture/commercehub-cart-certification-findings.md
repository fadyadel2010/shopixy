# PASS 16.3 Cart Certification Findings

Scope: PASS 16.3
Date: 2026-06-01

Verdict: CommerceHub cart is production-certifiable for the inspected runtime path. Public CommerceHub launch still inherits the broader PASS 15.6 and PASS 16.1B deployment, monitoring, retention, SMTP, and chaos-proof blockers.

## Cart Inventory

- Guest cart: browser-persisted Zustand cart, sanitized by `POST /api/commercehub/cart` when saved as guest.
- Authenticated cart: SQL-backed `CommerceHubCarts`, owned by `CommerceHubCustomerId` under the CommerceHub cart tenant scope.
- Cart merge: frontend guest-to-customer adoption preserves the guest snapshot, loads the server cart, merges duplicate lines, and persists through backend sanitation.
- Add item, remove item, update quantity, clear store, clear cart: implemented in `cart-store.ts`; authenticated persistence routes through cart APIs.
- Mini/summary/checkout payload: frontend summary is display intent only; pricing preview and checkout totals are backend recomputed.
- Saved cart: authenticated cart persists in SQL with optimistic versioning and checkout lock state.
- Explicitly not present: named saved-cart lists, wishlist-to-cart workflow, cart expiration policy for unlocked carts, durable cart mutation audit ledger.

## Add-To-Cart Findings

Customers can add items locally from product DTOs, but invalid lines do not become backend-authoritative cart lines. `CommerceHubCartService.SaveCartAsync` sanitizes requested listing/product/variant/store identity against current SQL, rejects invalid quantity, resolves listing/product/variant lineage, recomputes price through `ICommerceHubPriceAuthority`, and checks current stock. Deleted, unpublished, offline, inactive, mismatched, or invalid-variant lines are removed or blocked before persistence/checkout.

## Quantity Findings

Frontend clamps quantity to 1..100 and backend repeats the same limit through DTO validation plus service checks. Duplicate lines are grouped before pricing and checkout. Stock is rechecked from `IProductVariantStockRepository.GetAvailableQuantityAsync`; quantities above stock are reduced during cart sanitation or rejected during pricing/checkout.

## Guest Cart Findings

Guest carts are local-browser state. They survive reloads through persisted Zustand storage, but are not durable across devices, browser storage clearing, private browsing, or device loss. Guest save calls sanitize backend-visible data and return a server-shaped cart state without creating SQL ownership. This is safe but not durable.

## Cart Merge Findings

Guest-to-customer merge is implemented client-side with backend sanitation after merge. Duplicate lines reconcile by store and variant key, quantity is clamped, and the merged cart is saved under the authenticated customer version. This is safe for ordinary login/register adoption. Remaining debt is lack of a backend atomic merge endpoint for multi-device merge races.

## Pricing Findings

Cart pricing preview and checkout do not trust client totals. They reload SQL listings/products/variants/tenants, use `ICommerceHubPriceAuthority`, round currency server-side, resolve shipping selections server-side, validate promo code authority, and expose payment-method availability from backend state. Checkout rejects expected-unit-price drift with a conflict rather than silently accepting stale prices.

## Inventory Findings

Cart inventory is SQL-authoritative at sanitation, pricing, and checkout. Current product/variant public lineage, listing publication, tenant availability, and stock availability are revalidated before totals and order creation. Out-of-stock and reduced-stock transitions produce notices or conflicts. Inventory is not reserved at cart time; reservation remains checkout authority.

## Session Findings

Anonymous sessions have no server owner. Authenticated carts are resolved only from the CommerceHub customer authentication scheme and stored under `CommerceHubCustomerId`; raw client customer IDs are not accepted. Logout clears customer-scoped frontend cart state. Checkout locks the persisted cart and cart mutations fail while locked.

## Multi-Tab And Multi-Device Findings

Authenticated cart writes use optimistic versioning and return `CART_CONFLICT` when another tab/device has changed the cart. The frontend refreshes after conflict and exposes a review message. Checkout additionally uses customer and idempotency distributed locks plus a persisted cart lock/version check. Guest multi-tab state remains best-effort local storage and can diverge until refreshed.

## Frontend UX Findings

The frontend exposes quantity validation, out-of-stock validation, conflict refresh, locked-cart messaging, backend notices, price snapshots, cart summary, promo code, and checkout payload generation. It does not treat frontend totals as checkout authority. Remaining UX debt is stronger cross-tab broadcast refresh and clearer price-change/out-of-stock banners on every cart surface.

## Financial Findings

Frontend summary is not financial authority. Cart pricing preview and checkout recompute subtotal, shipping, promo, payment-method availability, and final totals server-side. Checkout persists order/sub-order/item snapshots after current price, inventory, shipping, promo, customer, vendor governance, readiness, and totals before persisting order snapshots.

## Auditability Findings

Cart incidents are partly investigable through API logs, correlation IDs, persisted cart version, checkout idempotency records, order snapshots, payment/finance evidence, and customer identity evidence. There is no durable append-only cart mutation ledger for add/remove/quantity/merge/price-refresh events.

## Chaos Findings

- Price changes during cart lifetime: detected by server recomputation and expected-price conflict at checkout.
- Stock changes during cart lifetime: detected by sanitation/pricing/checkout stock checks.
- Deleted/suppressed/unpublished products: fail closed through SQL listing/product/variant visibility checks.
- Offline products (IsOnlineVisible=false) or draft products (PublishStatus!=Published): fail closed during sanitization, pricing, and checkout. Sanitization hardening ensures parity with CalculateAsync/checkout authority.
- Variant deletion or semantic invalidity: fail closed through public variant lineage checks.
- Cart merge race: bounded by version conflict after frontend merge; backend atomic merge remains debt.
- Multi-tab/device race: bounded by optimistic cart version and checkout locks.
- Session expiration/logout: authenticated cart ownership comes from cookie/JWT validation; guest state remains local only.

## Monitoring Findings

Current telemetry is mostly log-based: cart save/clear/normalization logs, cart conflict/lock API outcomes, checkout idempotency/lock logs, and broader finance/search/readiness telemetry. Dedicated cart dashboards and alert packs for conflict rate, sanitation removals, price-drift conflicts, inventory-drift conflicts, merge failures, and guest-adoption failures remain missing.

## Runtime Hardening Performed

- **Cart sanitization product visibility hardening**: The `SanitizeAsync` method in `CommerceHubCartService` previously checked only `!product.IsActive` when deciding whether to remove items. This allowed products that were active but offline (`IsOnlineVisible=false`) or in draft status (`PublishStatus != Published`) to remain in saved carts, only to be rejected later at pricing or checkout. The sanitization check now enforces the same full publication visibility authority used by `CalculateAsync` and checkout: `!product.IsOnlineVisible || product.PublishStatus != PublishStatus.Published`. Items failing this check are removed with an `ITEM_REMOVED_UNAVAILABLE` notice for customer transparency.

## Regression Freeze Added

- `CommerceHubCartCertificationTests` — cart ownership, session behavior, frontend alignment, documentation, publication visibility hardening, checkout handoff authority
- `CommerceHubCartPricingCertificationTests` — price authority routing, financial total recomputation, price-drift detection
- `CommerceHubCartInventoryCertificationTests` — inventory authority, cart locking, sanitization notices, reservation timing

The freeze covers cart ownership, pricing authority, inventory authority, cart merge, guest adoption, session behavior, financial totals, checkout lock/version behavior, frontend/backend alignment, publication visibility, price-drift detection, sanitization notice codes, and inventory reservation timing.

## Architecture Updates

`.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md` now record PASS 16.3.

## Remaining Debt

- Durable append-only cart mutation ledger.
- Dedicated cart telemetry counters, dashboards, and alert rules.
- Backend atomic guest-cart adoption/merge endpoint for stronger multi-device race handling.
- Cross-tab broadcast refresh for guest cart and authenticated conflict events.
- Guest cart expiration/recovery policy.
- PostgreSQL-backed chaos tests for concurrent merge, price drift, stock drift, cart lock expiry, and stale checkout requests.

## Required Answers

| Question | Answer |
|---|---|
| Can customers reliably add products to cart? | Yes, for SQL-visible CommerceHub products; backend sanitation prevents invalid persistence. |
| Can invalid products reach carts? | They can exist transiently in local guest/browser state, but cannot survive backend sanitation, pricing, or checkout. |
| Can cart pricing drift? | Display snapshots can drift, but authoritative pricing is recomputed and checkout rejects stale expected prices. |
| Can cart inventory drift? | Display stock can drift, but sanitation/pricing/checkout revalidate stock and fail closed. |
| Can offline or draft products survive in carts? | No. Sanitization now enforces full publication visibility (IsOnlineVisible, PublishStatus=Published) with parity to pricing and checkout. |
| Is guest-cart behavior safe? | Safe as local intent; not durable across devices or storage loss. |
| Is cart merge safe? | Safe for normal login/register adoption; backend atomic merge remains debt for harder multi-device races. |
| Are cart totals trustworthy? | Yes, for the certified runtime path. Frontend totals are only display; backend pricing/checkout totals are authoritative. |
| Are cart incidents auditable? | Partially. Checkout/order/finance incidents are strong; pure cart mutations need a durable ledger. |
| Are critical cart risks present? | No critical runtime authority break was found. Remaining risks are operational maturity and guest-local durability. |
| Is CommerceHub cart production-ready? | Cart runtime is production-certifiable. Overall CommerceHub public launch remains blocked by broader certification debt. |

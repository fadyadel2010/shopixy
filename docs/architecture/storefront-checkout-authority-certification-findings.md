# PASS P2-B Storefront Checkout Authority Certification Findings

## 2026-06-29 PASS SF-PA5.1 Checkout Session Authority Certification

PointOfSale.Storefront Checkout Session Authority is production-certifiable after recovery
fail-closed hardening. Storefront frontend owns Checkout Intent, loading/error presentation,
navigation, step state, form candidates, and temporary UI state only. Backend
`CheckoutSessionController`, `CheckoutSessionService`, `ICartService.GetCartAsync`, SQL
`CheckoutSessions`, current backend cart authority, tenant currency authority, signed checkout
session tokens, and backend reconciliation own Checkout Session, Checkout Identity, Checkout
Admission, Checkout Snapshot, Cart Revalidation, Currency Lock, Session Lifetime, Recovery,
Persistence, and Projection Policy.

Runtime hardening performed: checkout recovery no longer returns stored `ItemsSnapshotJson`,
`CartTotal`, or session snapshot cart presentation when live cart revalidation is unavailable.
`RecoverByToken` must obtain a live backend cart through `ICartService.GetCartAsync`; otherwise it
returns `RECOVERY_CART_REVALIDATION_FAILED` and no cart payload. Storefront
`RecoverCheckoutPage` maps that fail-closed response to a cart-review path instead of hydrating a
stale checkout. Existing create/update session admission already ignores client-supplied
`itemsSnapshotJson`, `cartTotal`, and `currency`, rebuilding those fields from the currently owned
backend cart.

Exact runtime files modified for SF-PA5.1:

- `PointOfSale.API/Controllers/CheckoutSessionController.cs`
- `PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx`

Regression freezes added/updated:

- `PointOfSale.Tests/Security/StorefrontProductionBlockerFixTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontCheckoutAuthorityCertificationTests.cs`

SF-PA5.1 required answers: Storefront frontend cannot create Checkout Sessions, bypass cart
revalidation, promote LocalStorage/sessionStorage/React/Zustand state to checkout authority, or make
Redis checkout authority. Recovery converges to live backend cart/session authority or fails closed.
Checkout Intent begins when Storefront navigates from cart to `/checkout` and calls
`initCheckoutSession()` after backend cart hydration. Checkout Authority begins in
`CheckoutSessionController.CreateOrResume` after tenant/customer/session owner validation and live
`ICartService.GetCartAsync` admission. Checkout Snapshot begins when the backend builds
`BuildAuthoritativeItemsSnapshotJson(authoritativeCart)` and persists it through
`CheckoutSessionService`. Storefront presentation begins only after backend DTO admission returns.

Date: 2026-06-08

## Verdict

Storefront Checkout Authority is production-certifiable for the inspected tenant storefront and
CommerceHub runtime paths after this hardening. Checkout owns eligibility authority. PostgreSQL
catalog, category, customer, tenant, payment configuration, shipping configuration, promotion,
coupon, inventory, idempotency, and order rows remain authoritative. Redis carts, checkout-session
snapshots, MongoDB, frontend state, DTO totals, route values, headers, and recovery payloads are
intent or evidence only.

## Findings

### Cart Ownership And Identity

- Tenant storefront carts are keyed by tenant plus canonical customer or guest-session owner.
- Authenticated customer headers must match current authenticated customer and tenant authority.
- CommerceHub authenticated carts are SQL-owned by the current CommerceHub customer and protected
  by optimistic versioning and checkout locks.
- Tenant storefront checkout revalidates current SQL customer tenant ownership and active state.
  Guest checkout requires the exact session-owned cart.
- Cart merge, recovery, stale sessions, and identity switching cannot transfer checkout ownership.

### Product And Quantity Eligibility

- Tenant storefront final checkout now requires current nondeleted, active, online-visible,
  `Published` SQL product state, active variant state, matching product/variant ownership, and live
  full category lineage.
- CommerceHub checkout requires current published listing, product, and variant authority, active
  tenant/vendor governance, and now live CommerceHub category lineage.
- Quantities are positive and bounded, regrouped server-side, checked against SQL stock, and
  reserved through certified inventory authority before commit.

### Pricing, Coupon, Promotion, Shipping, And Payment

- Product prices are recomputed from current SQL variant/listing price authority.
- Client prices and expected totals are stale-client evidence only; mismatches fail closed.
- Coupons are resolved by server-issued identity, code, customer eligibility, scope, expiration,
  usage, and reservation authority.
- Promotions are recomputed server-side and calculation failure fails closed.
- Shipping options and costs are recomputed from current backend authority.
- Payment eligibility is recomputed from active tenant configuration, provider configuration,
  amount limits, and multi-store availability.

### Conversion, Replay, And Concurrency

- Tenant storefront conversion uses owned distributed locks, durable request/snapshot
  idempotency, serializable final authority revalidation, active-cart SQL uniqueness, inventory
  reservation, and transactional order creation.
- CommerceHub conversion uses customer/idempotency locks, request-hash-bound SQL idempotency,
  saved-cart locking/version checks, serializable creation, and inventory reservation.
- Replays return stored results or fail on payload conflict; they do not duplicate orders,
  discounts, or inventory effects.

### Abandoned Checkout, Currency, And Tenant Isolation

- Checkout-session autosave now ignores client item snapshot, total, and currency authority and
  rebuilds those fields from the currently owned live cart.
- Recovery tokens are tenant-bound, expiring, nonce-bound, and rejected after conversion.
- Recovery restores presentation state only. Final checkout revalidates all current eligibility.
- Tenant storefront currency must match current SQL tenant currency. CommerceHub is EGP-only.
- Cart, session, checkout, coupon, shipping, payment, inventory, and order access retain tenant or
  platform-scope ownership predicates.

### Adversarial Review

- Cart, DTO, price, total, coupon, promotion, shipping, payment, currency, identity, route, and
  tenant tampering are recomputed or rejected.
- Duplicate submission, concurrent checkout, stale cart, stale recovery, and idempotency-key reuse
  converge or fail closed.
- Checkout cannot bypass inventory reservation or promote stale recovery snapshots into order
  authority.

## Runtime Hardening Performed

- Added `PublishStatus.Published` and live category-lineage eligibility to tenant storefront final
  SQL checkout authority.
- Added live CommerceHub category-lineage validation inside marketplace checkout.
- Replaced client-supplied checkout-session item/total/currency autosave fields with an owned live
  cart snapshot.

## Exact Runtime Files Modified

- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs`
- `PointOfSale.API/Controllers/CheckoutSessionController.cs`

## Regression Freeze

- `PointOfSale.Tests/Architecture/StorefrontCheckoutAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Verification

- API build passed with 0 errors; existing warnings remain.
- Targeted P2-B, P2-A, CommerceHub checkout, checkout concurrency, coupon, promotion,
  currency, stock, and privacy authority suites passed: 70 passed, 0 failed, 0 skipped.

## Required Answers

- Who owns checkout authority? Checkout owns eligibility authority using current backend SQL
  authority from the certified domains.
- Can cart ownership be bypassed? No.
- Can clients control prices? No.
- Can clients control discounts? No.
- Can clients control shipping cost? No.
- Can clients force payment methods? No.
- Can checkout create duplicate orders? No on the certified replay and concurrency paths.
- Can abandoned checkout bypass current validation? No.
- Can mixed currencies corrupt checkout? No.
- Can tenant leakage occur? No on inspected paths.
- Can replay attacks corrupt checkout? No.
- Can checkout bypass inventory validation? No.
- Were runtime code changes required? Yes.
- Is Storefront Checkout Authority production-certifiable? Yes, for the inspected runtime scope.

## Remaining Debt

- Production PostgreSQL chaos evidence for simultaneous cart mutation, coupon consumption,
  promotion evaluation, stock exhaustion, checkout replay, and recovery remains operational work.
- Dedicated checkout dashboards and alert packs remain incomplete.
- Guest carts remain Redis/browser-session durability rather than long-term durable customer state.

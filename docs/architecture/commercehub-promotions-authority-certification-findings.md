# PASS M10 CommerceHub Promotions Authority Certification Findings

Scope: PASS M10 CommerceHub Promotions Authority

Status: CommerceHub Promotions Authority is production-certifiable for inspected runtime paths after promo scope-publication hardening.

## Promotion findings

CommerceHub promotion identity is SQL-owned by `PromoCodes`. Promo codes are globally unique while live, normalized server-side, constrained by SQL discount/usage/scope checks, and created only through the Super Admin promo workflow. DTO code strings are lookup evidence only.

## Eligibility findings

Promo eligibility is recomposed from current SQL promo definition, current checkout line commission snapshots, vendor/category scope, active flag, expiration, total usage, customer usage, and current CommerceHub checkout catalog authority. Runtime hardening now requires vendor-scoped promos to point at current SQL marketplace-eligible vendor authority and category-scoped promos to point at live SQL CommerceHub category lineage before create/update/reactivation.

## Publication findings

Promotion publication is SQL `PromoCodes.IsActive` plus current Super Admin scope validation. Inactive, deleted, vendor-ineligible, or inactive-category promos cannot be newly published or reactivated on inspected paths.

## Expiration findings

`PromoService` checks SQL `ExpiryDate` during cart pricing, checkout validation, and final consumption. Expired promos fail closed and cannot be resurrected by DTOs, cached cart pricing, abandoned checkout state, or projections.

## Usage-limit findings

Usage limits are SQL-owned by `PromoCodeUsages` plus row-locked promo authority. Checkout locks the promo row, counts total/customer usage, enforces `UsageLimit` and `MaxUsagePerCustomer`, and writes immutable usage lineage. SQL uniqueness permits only one live promo usage per marketplace order and one usage per promo/order pair.

## Stacking findings

CommerceHub runtime applies at most one promo code. `PromoCalculationEngine` calculates one scoped discount per eligible vendor group from backend commission lines; there is no DTO-selected stacking winner.

## Checkout findings

`CommerceHubCheckoutService` revalidates promo code authority during serializable checkout, recomputes commission discount from current line snapshots, persists order/sub-order/item promo discount snapshots, and aborts checkout if final promo consumption cannot be claimed.

## Pricing-boundary findings

Promotions reduce platform commission snapshots only; they do not create order subtotal, payment, settlement, payout, treasury, refund, or ledger truth. Order/sub-order/item snapshots remain the financial input consumed by payment, earnings, ledger, refund, and settlement authorities.

## Projection findings

No inspected CommerceHub promo application path consumes MongoDB, Redis, Elasticsearch, analytics, or frontend state as authority. Dashboard and financial reports join back to SQL `PromoCodeUsages` and order snapshots as derivative reads.

## Isolation findings

Vendor-scoped promos can affect only matching SQL vendor checkout lines, and category-scoped promos can affect only matching live SQL category checkout lines. Runtime hardening blocks creation/update/reactivation against foreign, inactive, suspended, banned, unverified, unapproved, or deleted vendor authority and inactive category lineage.

## Concurrency findings

Checkout validates and consumes promos inside the SQL checkout transaction, row-locks the promo row on relational providers, and collapses replay through SQL usage uniqueness. Concurrent usage-limit races must serialize on the promo row before usage insertion.

## Historical findings

Historical marketplace orders retain immutable promo code and promo discount snapshots. `PromoCodeUsages` remains immutable consumption lineage; later promo deactivation/expiration does not mutate historical order truth.

## Adversarial findings

Coupon/promo forgery, discount inflation, usage-limit bypass, eligibility bypass, stacking abuse, expired-promo resurrection, vendor escalation, cross-vendor application, tenant guessing, DTO tampering, route tampering, stale cart pricing, and projection replay were reviewed against inspected runtime paths. No runtime bypass remains after scope-publication hardening.

## Runtime hardening performed

- `SuperAdminPromoCodeManagementService` now validates vendor-scoped promos through `CommerceHubVendorAuthority.LoadAsync` and requires current marketplace eligibility.
- `SuperAdminPromoCodeManagementService` now validates category-scoped promos through `CommerceHubCategoryLineageAuthority.IsLiveAsync`.
- Promo reactivation now revalidates current vendor/category scope authority before setting `IsActive = true`.
- Empty scope IDs are normalized to `null` so global promos do not persist forged empty scope targets.

## Exact runtime files modified

- `PointOfSale.API/SuperAdmin/Services/SuperAdminPromoCodeManagementService.cs`

## Regression freezes added

- `PointOfSale.Tests/Architecture/CommerceHubPromotionsAuthorityCertificationTests.cs`

## Architecture updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/commercehub-promotions-authority-certification-findings.md`

## Required answers

- Who owns promotion authority? PostgreSQL `PromoCodes`, `PromoCodeUsages`, current CommerceHub vendor/category/catalog/checkout SQL authority, and immutable marketplace order/sub-order/item snapshots own promotion authority.
- Can promotions be forged? No on inspected paths; submitted codes must resolve to current SQL promo authority.
- Can eligibility be bypassed? No inspected bypass remains; eligibility is recomputed from SQL promo, vendor/category scope, checkout lines, usage, and catalog authority.
- Can expired promotions apply? No; expiration is checked at pricing, checkout validation, and final consumption.
- Can usage limits be bypassed? No inspected bypass remains; row locks, SQL counts, immutable usage rows, and uniqueness collapse replay/concurrency.
- Can discounts stack improperly? No inspected CommerceHub path stacks multiple promos.
- Can checkout consume invalid promotions? No; checkout aborts if final SQL consumption fails.
- Can promotions become financial authority? No; promotions affect commission snapshots only and cannot create payment/order/settlement/payout/ledger truth.
- Can tenant leakage occur? No inspected path allows vendors or tenants to affect foreign promo authority; Super Admin creation scope is SQL validated.
- Were runtime code changes required? Yes, Super Admin promo scope-publication hardening.
- Is CommerceHub Promotions Authority production-certifiable? Yes, for inspected runtime paths.

# PASS AF-PA5.3 Checkout Attribution, Order Line Admission & Commission Candidate Certification

## Checkout Findings

Checkout is backend-owned. `CheckoutService.ProcessCheckoutAsync` accepts checkout intent, shipping selection, payment selection, and expected pricing as candidates only. The final order transaction re-enters SQL authority through `RevalidateCheckoutAuthorityInsideTransactionAsync`, reloads shipping/payment/current catalog pricing authority, rejects stale totals, and only then creates order lines.

Cart affiliate evidence is not trusted by checkout. The checkout path does not consume `CartItemDto.IsAffiliateEligibleLine`, line `AffiliateAccountId`, line `PromotionLinkId`, or Redis cart attribution as financial authority. Affiliate order attribution is resolved from middleware-protected/current backend evidence and revalidated inside the final serializable transaction.

## Order Line Findings

Every order line now receives independent backend Order Line Admission before it can carry a commission snapshot. `LoadAffiliateCommissionPolicySnapshotsAsync` now returns `AffiliateOrderLineAdmissionSnapshot` evidence only after current SQL product lifecycle, enabled policy, active affiliate category, and final affiliate authority pass.

For v2 promotion-link attribution, line admission is scoped to the current backend promotion link product through `PromotionLinkProductId`. A line whose product is not the current active promotion link product is not admitted as a commission candidate. Normal order lines remain ordinary order items with all affiliate commission snapshot fields null.

## Commission Candidate Findings

A Commission Candidate begins at checkout-time `OrderItem.AffiliateCommissionEligible = true` plus the required immutable snapshot fields. That assignment happens only in `RefreshOrderItemAuthoritySnapshots` after final transaction revalidation and after `AffiliateOrderLineAdmissionSnapshot` is loaded from current SQL authority.

Later commission recording must consume the immutable order-item snapshot; it must not recalculate from current product policy, cart evidence, frontend DTOs, or order-level attribution alone.

## Mixed Order Findings

Mixed orders are valid. Affiliate-admitted lines can carry commission snapshots while normal lines stay non-eligible. Normal lines cannot inherit commission from the order header, affiliate journey, cart, URL, cookie, Redis, or another order line.

## Financial Findings

Current price and stock remain checkout-owned SQL authority. `RecalculatePricesInternalAsync`, `ValidateAndReserveInventoryAsync`, `RefreshOrderItemAuthoritySnapshots`, and the order-item snapshot constraint compose the financial boundary. Stale cart prices, stale line attribution, cached DTOs, request tampering, and modified frontend state cannot create a commission candidate.

## Lifecycle Findings

Affiliate suspension, deleted or inactive promotion links, disabled products, inactive categories, archived/unpublished/hidden products, price changes, and inventory loss all re-enter backend authority before candidate creation. If final authority is absent, checkout fails closed or persists ordinary non-commission order lines.

## Cache Findings

Cart cache, checkout DTOs, frontend state, browser history/BFCache, session storage, and Redis are candidate or projection evidence only. Cached affiliate cart evidence cannot become financial authority because checkout does not use cart line attribution to set `AffiliateCommissionEligible`.

## Failure Findings

400/401/403/404/409/422/500/timeouts and rollback paths cannot create Commission Candidates. Candidate creation is inside the order transaction; failed validation, failed final affiliate authority revalidation, stale promotion-link authority, stale pricing, stale stock, and transaction rollback prevent order-item snapshot persistence.

## Adversarial Findings

Modified checkout DTOs, modified cart evidence, forged `AffiliateAccountId`, forged `PromotionLinkId`, replayed checkout, replayed old cart, and replayed old promotion-link evidence cannot fabricate Commission Candidates. The frontend has no accepted authority field for order-line attribution, and checkout ignores cart-line attribution fields for financial admission.

## Checkout Admission Pipeline

Journey Attribution -> Eligible Cart Line -> Checkout intent -> Current SQL Revalidation -> Order Line Admission -> Commission Candidate -> Order Persistence.

Order Line Admission occurs in `LoadAffiliateCommissionPolicySnapshotsAsync` after `RevalidateResolvedAffiliateAuthorityInsideFinalTransactionAsync` and before `RefreshOrderItemAuthoritySnapshots`.

Commission Candidate begins in `RefreshOrderItemAuthoritySnapshots` when an admitted line sets `OrderItem.AffiliateCommissionEligible = true` with a valid immutable commission snapshot.

## Runtime Hardening Performed

- Added `PromotionLinkProductId` to checkout affiliate authority so final checkout can bind v2 commission candidates to the current SQL promotion-link product.
- Reworked checkout commission snapshot loading into `AffiliateOrderLineAdmissionSnapshot`.
- Required current SQL product lifecycle and active affiliate category authority during checkout order-line admission.
- Scoped v2 promotion-link commission candidates to the linked product.
- Kept non-admitted and normal lines cleared/null for all affiliate commission snapshot fields.

## Exact Runtime Files Modified

- `PointOfSale.API/Services/Checkout/CheckoutService.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/AffiliatePromotionLinkResolutionStorefrontEntryCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Required Answers

- Who owns Checkout Attribution? Backend current SQL and protected middleware evidence, revalidated in checkout.
- Can Checkout trust Cart evidence? No.
- Can frontend fabricate Commission Candidates? No.
- Can normal order lines inherit commission? No.
- Can stale cart evidence create financial authority? No.
- Where does Order Line Admission occur? `LoadAffiliateCommissionPolicySnapshotsAsync`, now returning `AffiliateOrderLineAdmissionSnapshot`.
- Where does Commission Candidate begin? `RefreshOrderItemAuthoritySnapshots` when `AffiliateCommissionEligible` and immutable snapshot fields are set.
- Were runtime changes required? Yes.
- Is Checkout Attribution production-certifiable? Yes, after AF-PA5.3 hardening.

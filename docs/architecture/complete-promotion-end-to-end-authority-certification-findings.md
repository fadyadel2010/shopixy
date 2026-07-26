# PASS SF-PRM.FINAL Complete Promotion End-to-End Authority Certification

Date: 2026-07-02

## Verdict

Complete Promotion Domain is production-certifiable. No runtime behavior change was required.

Backend owns Promotion Identity, Lifecycle, Rules, Eligibility, Winning Promotion, Pricing, Financial Authority, Reservation, Usage, Usage Confirmation, Audit, Recovery, Projection, Persistence, and SQL Truth. PointOfSale.Web and PointOfSale.Storefront own candidate entry, selection, presentation, navigation, rendering, retry, refresh, and loading/error state only.

Accepted Promotion Authority exists only after backend admission.

## Overall findings

Complete Promotion runtime remains backend-owned from admin creation/edit through activation, storefront qualification, pricing preview, checkout, reservation, payment success/failure, usage confirmation, recovery, projection rebuild, audit review, and presentation. The inspected runtime composes prior certified Promotion slices without weakening authority boundaries:

- SF-PROMO.READ: backend-owned admin read and DTO admission.
- SF-PROMO.LIFECYCLE: backend-owned lifecycle mutation, RowVersion, and If-Match authority.
- SF-PROMO.RULE: backend-owned rule and eligibility admission.
- SF-PRM.FIN: backend-owned pricing, winning promotion, financial validation, and SQL order snapshot.
- SF-PRM.RECOVERY-AUDIT: backend-owned recovery, usage confirmation, audit, projection, and replay protection.

## Read findings

Promotion Read begins in SQL `Promotions` under trusted tenant/session/role authority. `PromotionsController.GetPromotions`, `GetPromotion`, and `GetStats` read tenant-owned non-deleted SQL rows, usage rows, and tenant currency, execute search/filter/sort/pagination on the backend, emit no-store DTOs, and require frontend admission through `adminPromotionReadAuthority` before presentation.

Frontend list/detail/stats state cannot become read authority. React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, Mongo projection documents, stale DTO replay, and modified DTO evidence can only trigger refetch, rejection, loading, or error presentation.

## Lifecycle findings

Promotion Lifecycle is backend-owned. Create, edit, activate, deactivate, and archive enter `PromotionsController` after trusted tenant/session authority and Owner/Admin/Manager authorization. Existing-promotion mutations require accepted backend RowVersion evidence through mandatory `If-Match`; stale, missing, malformed, replayed, or modified evidence fails closed before SQL persistence.

Admin frontend lifecycle state is candidate intent only.

## Eligibility findings

Promotion Rules and Eligibility are backend-owned. `ValidatePromotionRuleAdmissionAsync` validates and canonicalizes Product, Category, and Cart scope candidates before create/update persistence. Product targets must be active, non-deleted, current-tenant SQL products; category targets must be non-deleted, current-tenant SQL categories; Cart scope cannot carry product/category targets.

Unsupported brand, customer, customer-group, store, warehouse, sales-channel, and explicit exclusion fields remain outside accepted Promotion persistence in the inspected runtime.

## Financial findings

Winning Promotion, Discount Type, Discount Value, Percentage, Fixed Amount, Buy X Get Y where modeled, Free Shipping where modeled, priority, exclusivity, stacking decision, tax, shipping, payment fee, currency, expected pricing, and grand total are backend-owned. Storefront submits cart, shipping, payment, country, coupon, and expected-pricing candidates only.

Pricing begins in backend `CheckoutService.PreviewPricingAsync` and final checkout transaction revalidation. `PromotionEngine.CalculateAsync` loads current active tenant SQL promotions, validates discount configuration, eligibility, minimum order, usage capacity, priority, and exclusivity/stacking policy, then `DetermineWinner` selects the backend winner. Final checkout revalidates pricing inside the serializable SQL transaction, rejects expected-pricing mismatches, and persists SQL order pricing snapshots only after backend financial admission.

## Recovery findings

Recovery is backend-owned. Storefront recovery validates signed recovery tokens, resolves current SQL abandoned-cart ownership, and recomposes a live backend cart. Stored abandoned-cart snapshots, browser storage, Redis, Mongo, and stale DTOs are not recovery source. If live backend cart admission fails, recovery fails closed.

Payment failure does not confirm usage. Payment success confirms usage only after payment/order success authority re-enters current SQL promotion authority.

## Audit findings

Audit begins only in backend append-only SQL `AuditLogs` and tenant-scoped no-store audit reads/exports. Frontend audit pages request and render backend evidence only; they do not mutate audit rows or hydrate audit evidence from browser storage.

## Projection findings

Projection begins only in `PromotionProjectionConsumer` or `DiscountReadModelRepairService.RebuildPromotionsAsync` after SQL authority reload. Mongo promotion documents, product/category promotion decorations, Redis invalidation, and search sync are derivative visibility evidence only.

Projection event fences, SQL version validation, freshness filters, SQL-derived current usage, stale-document deletion, and SQL-first repair prevent replay, consumer restart, Redis flush, Mongo rebuild, or injected projection documents from becoming Promotion Authority.

## Cross-system findings

Coupons enter promotion pricing only as backend-prevalidated coupon discount evidence. Checkout reserves promotion capacity only after backend pricing/order authority. Orders persist promotion identity/discount snapshots as SQL evidence. Payments confirm promotion usage only after success and current SQL promotion revalidation.

Smart Offers, Affiliate, Marketplace, Inventory, Builder, CMS, Analytics, and adjacent domains cannot overwrite Promotion Authority, and Promotion cannot overwrite their authority. Cross-system handoffs carry backend-admitted candidates, snapshots, events, or presentation evidence only.

## Runtime hardening performed

No new runtime behavior changes were required for SF-PRM.FINAL. The final review relies on the already hardened runtime boundaries from SF-PROMO.READ, SF-PROMO.LIFECYCLE, SF-PROMO.RULE, SF-PRM.FIN, and SF-PRM.RECOVERY-AUDIT.

## Exact runtime files modified

None.

## Regression freezes added

- `PointOfSale.Tests/Architecture/PromotionEndToEndAuthorityCertificationTests.cs`

## Architecture documentation updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/complete-promotion-end-to-end-authority-certification-findings.md`

## End-to-End pipeline

Promotion Candidate -> Backend Admission -> Rule Validation -> Eligibility Validation -> Pricing Engine -> Winning Promotion -> Financial Validation -> Checkout -> Reservation -> Payment -> Promotion Usage -> Audit -> Outbox -> Projection -> Recovery -> Accepted DTO -> Presentation.

- Promotion begins: backend SQL `Promotions` and admin/storefront candidate submission that must enter backend admission.
- Authority begins: backend trusted tenant/session/customer/role admission and current SQL promotion authority.
- Validation begins: `PromotionsController` business/rule validation and checkout/storefront backend cart/catalog/promotion validation.
- Pricing begins: `CheckoutService.PreviewPricingAsync` and final transaction revalidation before `StorefrontPricingService.CalculateCheckoutAsync`.
- Winning Promotion begins: `PromotionEngine.CalculateAsync` / `DetermineWinner`.
- Reservation begins: `ReserveAppliedOfferPromotionAsync` inside the serializable checkout SQL transaction.
- Usage begins: payment success `ConfirmOfferPromotionUsageAsync` after current SQL promotion/order/payment revalidation.
- Audit begins: backend append-only SQL audit/event/outbox authority.
- Projection begins: `PromotionProjectionConsumer` or SQL repair after SQL authority reload.
- SQL persistence begins: backend promotion write transactions, checkout order transaction, and payment lifecycle `PromotionUsage` insert.
- Presentation begins: PointOfSale.Web or PointOfSale.Storefront only after accepted backend DTO admission.

## Authority map

Identity Boundary: backend SQL `Promotions`. Recovery source: SQL. Failure behavior: 404/403/reject or no presentation.

Lifecycle Boundary: backend `PromotionsController` plus SQL RowVersion/If-Match. Recovery source: no-store SQL refetch. Failure behavior: 400/404/409/428.

Rule Boundary: backend rule validation/canonicalization. Recovery source: SQL promotions plus SQL catalog. Failure behavior: reject mutation.

Eligibility Boundary: backend SQL promotion/catalog validation. Recovery source: SQL. Failure behavior: reject mutation or pricing exclusion.

Pricing Boundary: backend checkout/pricing services. Recovery source: live backend cart and SQL promotion/catalog authority. Failure behavior: fail closed pricing/checkout.

Financial Boundary: backend pricing, currency, tax, shipping, payment, expected-pricing, and SQL order snapshot. Recovery source: SQL order/cart/pricing authority. Failure behavior: mismatch rejection or checkout abort.

Reservation Boundary: backend serializable checkout SQL transaction. Recovery source: SQL order/reservation evidence. Failure behavior: no reservation or transaction rollback.

Usage Boundary: backend payment lifecycle SQL transaction. Recovery source: SQL payment/order/promotion truth. Failure behavior: no usage insert or duplicate collapse.

Audit Boundary: backend append-only SQL `AuditLogs`. Recovery source: SQL audit rows. Failure behavior: no-store read/export failure.

Recovery Boundary: backend recovery controllers/services. Recovery source: live backend cart plus SQL order/payment/promotion truth. Failure behavior: fail closed.

Projection Boundary: backend event consumer/repair. Recovery source: SQL truth. Failure behavior: fenced replay, stale projection, rebuild, or no side effect.

Presentation Boundary: frontend admitted DTO rendering. Recovery source: backend refetch/admitted DTO. Failure behavior: loading/error/empty state only.

## Explicit certification answers

- Did you review ONLY complete Promotion runtime? Yes.
- Who owns Promotion Identity? Backend SQL `Promotions`.
- Who owns Lifecycle? Backend.
- Who owns Rules? Backend.
- Who owns Eligibility? Backend.
- Who owns Winning Promotion? Backend `PromotionEngine`.
- Who owns Pricing? Backend checkout/pricing services.
- Who owns Financial Authority? Backend.
- Who owns Reservation? Backend checkout SQL transaction.
- Who owns Usage? Backend payment lifecycle SQL authority.
- Who owns Audit? Backend append-only SQL audit authority.
- Who owns Recovery? Backend SQL/cart/order/payment/promotion authority.
- Who owns Projection? Backend SQL-derived projection consumers/repair; projection is derivative.
- Who owns SQL Truth? Backend PostgreSQL.
- Can frontend fabricate Promotion Authority? No.
- Can frontend fabricate Winning Promotion? No.
- Can frontend fabricate Discount? No.
- Can replay fabricate Promotion Usage? No.
- Can cache become Promotion Authority? No.
- Can Redis become Promotion Authority? No.
- Can Mongo become Promotion Authority? No.
- Can Projection become Promotion Authority? No.
- Can Recovery diverge from SQL? No inspected path allows divergence; recovery re-enters SQL/live backend authority or fails closed.
- Can another domain overwrite Promotion Authority? No.
- Where does Promotion begin? SQL `Promotions` plus candidate submission before backend admission.
- Where does Authority begin? Backend trusted tenant/session/customer/role admission against SQL truth.
- Where does Validation begin? Backend controller/service validation.
- Where does Pricing begin? Backend checkout pricing preview/final transaction revalidation.
- Where does Reservation begin? `ReserveAppliedOfferPromotionAsync` inside checkout SQL transaction.
- Where does Usage begin? `ConfirmOfferPromotionUsageAsync` after payment success.
- Where does Audit begin? Backend append-only SQL audit/outbox authority.
- Where does Projection begin? SQL-derived projection consumer or repair.
- Where does SQL persistence begin? Backend write transactions.
- Where does Presentation begin? Frontend after accepted backend DTO admission.
- Were runtime changes required? No.
- Is complete Promotion Domain production-certifiable? Yes.

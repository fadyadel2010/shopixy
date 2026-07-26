# PASS AF-PA5.2 Cart Attribution, Eligible Product Admission & Mixed Cart Certification

Cart Attribution, Eligible Product Admission, and Mixed Cart behavior are production-certifiable after runtime hardening. Backend cart service line admission now owns affiliate eligibility per cart line. Frontend add/remove/quantity/cart rendering remains intent and presentation only.

## Cart Findings

`StorefrontCartController` accepts add-to-cart intent only. It reads backend request-scoped affiliate evidence from `AffiliateModeResolver` and passes affiliate account, referral code, and promotion-link evidence into `CartService`. The add DTO does not carry affiliate eligibility, line attribution, tenant authority, category authority, or commission authority.

The previous cart-wide affiliate-mode rejection is removed. A valid affiliate journey no longer turns the entire cart into an affiliate-only cart. Non-affiliate products can be admitted to the cart as ordinary cart lines, but they receive no affiliate line evidence.

## Line Admission Findings

Cart Line Admission occurs in `CartService.ResolveAffiliateCartLineAdmissionAsync`. Each line must independently pass current backend authority:

- active affiliate account
- current active tenant promotion-link evidence when a promotion link is present
- current non-deleted active published storefront-visible product
- enabled non-deleted affiliate policy
- active non-deleted affiliate category
- positive product or variant storefront price
- available stock or backorder allowance

`CartItemDto` now carries backend-populated line evidence: `IsAffiliateEligibleLine`, `AffiliateAccountId`, `AffiliateReferralCode`, and `PromotionLinkId`. These fields are response evidence only and are recalculated by backend cart admission.

## Mixed Cart Findings

Mixed carts are supported. Promoted affiliate products and other currently affiliate-eligible products can be line-admitted. Non-affiliate products remain in the cart without affiliate line evidence. A non-eligible line cannot inherit attribution merely because the cart or browser journey is affiliate-attributed.

## Lifecycle Findings

Cart reads re-enter line admission during `RevalidateCartProductsAsync`. Product deletion, archive/unpublish/hide, affiliate disablement, category disablement, price removal, stock loss, affiliate suspension, inactive promotion link, or mismatched promotion-link evidence clears affiliate line attribution. Normal cart product lifecycle rules still decide whether the item remains purchasable or is removed.

## Cache Findings

Redis cart state is cache and intent state, not affiliate authority. Cached line evidence is revalidated from SQL on cart read. Browser cache, React state, session storage, guest cart state, and multi-tab state cannot preserve stale affiliate eligibility because the backend refreshes or clears line evidence before projection.

## Failure Findings

400, 401, 403, 404, 409, 422, 500, timeout, Redis failure, and offline cases cannot fabricate affiliate lines. Failed cart updates do not create backend line evidence. If backend line admission cannot prove current authority, it returns `NotEligible` and clears attribution rather than trusting cached evidence.

## Adversarial Findings

Modified cart DTOs, product IDs, affiliate context, old cart payloads, replayed add requests, and modified line eligibility cannot create affiliate cart items. `AddCartItemRequest` remains product/variant/quantity intent only; line attribution evidence is assigned by `CartService` from backend context and current SQL eligibility.

## Cart Admission Pipeline

Journey Attribution

-> `AffiliateReferralMiddleware` validates protected evidence and current SQL authority

-> `AffiliateModeResolver` stores request-scoped backend evidence

-> Add To Cart intent reaches `StorefrontCartController`

-> Backend product and variant validation in `CartService`

-> Current Eligibility Validation in `ResolveAffiliateCartLineAdmissionAsync`

-> Line Admission assigns or clears per-line affiliate evidence

-> Cart Projection returns mixed cart lines with independent eligibility

-> Cart Rendering presents backend evidence only

Cart Line Admission occurs in `PointOfSale.API/Services/Cart/CartService.cs` inside `ResolveAffiliateCartLineAdmissionAsync`, with re-entry from `RevalidateCartProductsAsync`.

## Runtime Hardening Performed

- Removed cart-wide affiliate-mode rejection of non-affiliate products.
- Added per-line backend affiliate evidence to `CartItemDto`.
- Changed cart add paths to pass backend-resolved affiliate account/referral/promotion-link evidence instead of a boolean mode flag.
- Added SQL-current line admission in `CartService`.
- Revalidated and cleared cached line evidence during cart reads.
- Preserved guest-merge line evidence only as candidate evidence that must re-enter backend admission.

## Exact Runtime Files Modified

- `PointOfSale.API/DTOs/Cart/CartDtos.cs`
- `PointOfSale.API/Services/Cart/ICartService.cs`
- `PointOfSale.API/Services/Cart/CartService.cs`
- `PointOfSale.API/Controllers/StorefrontCartController.cs`
- `PointOfSale.API/Controllers/StorefrontFunnelController.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/AffiliatePromotionLinkResolutionStorefrontEntryCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Required Answers

- Who owns Cart Attribution? Backend cart admission, current SQL eligibility, and protected/request-scoped affiliate evidence.
- Can frontend fabricate affiliate cart items? No.
- Can non-affiliate products inherit attribution? No.
- Can mixed carts preserve correct eligibility? Yes, each line is independently admitted.
- Can stale cart lines survive eligibility loss? Stale cart items may remain only as ordinary cart intent when still purchasable; stale affiliate line attribution is cleared.
- Can cache become cart authority? No.
- Where does Cart Line Admission occur? `CartService.ResolveAffiliateCartLineAdmissionAsync`, re-entered by `RevalidateCartProductsAsync`.
- Were runtime changes required? Yes.
- Is Cart Attribution production-certifiable? Yes, for Cart Attribution, Eligible Product Admission, and Mixed Cart behavior.

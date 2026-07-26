# PASS AF-PA2.5 Affiliate Product Eligibility Business Rules & Lifecycle Certification Findings

## Certification

Affiliate Product Eligibility Business Rules and Lifecycle is production-certifiable after backend lifecycle eligibility hardening.

Backend owns every effective eligibility decision. Frontend owns presentation, candidate intent, display, navigation, and guidance only.

## Lifecycle Findings

- Effective affiliate eligibility now requires current backend Product SQL authority: non-deleted, active, `PublishStatus.Published`, `IsOnlineVisible`, positive storefront price, and available stock or backorder allowance.
- Draft products cannot become effectively affiliate eligible.
- Archived products cannot remain effectively affiliate eligible.
- Deleted products cannot remain effectively affiliate eligible.
- Restored products recover only by re-entering current backend lifecycle/category/price/stock authority; stored policy configuration alone is not effective eligibility.

## Status Findings

- Active plus published plus visible is required.
- Inactive, hidden, archived, deleted, draft, and unavailable products fail closed for effective eligibility.
- Out-of-stock products are not affiliate available unless backend product authority allows backorders.
- Future scheduled products are not eligible before backend publication makes them currently published and visible.

## Category Findings

- Enabled affiliate policy requires an affiliate category.
- Enablement validates active category authority.
- Effective reads and public discovery recomposition now require active category authority, so inactive/invalid category mappings cannot keep public affiliate availability alive.

## Pricing Findings

- Enablement requires positive storefront price authority.
- Zero or negative effective price fails closed.
- Variant products require a current active semantic-owner variant with positive effective storefront price.

## Commission Findings

- Commission remains backend/domain guarded at `0.0001` through `0.50`.
- Zero, negative, and above-maximum commission cannot create a valid policy.
- Max commission cap must be positive when present.

## Visibility Findings

- Hidden products cannot be effectively affiliate eligible.
- Public affiliate discovery recomposes Elasticsearch candidates from SQL/product/category authority before display.
- Product lifecycle events trigger public affiliate visibility sync; stale documents are removed or fail closed at recomposition.

## Promotion Link Findings

- Promotion links can identify referral/attribution candidates only.
- Promotion links do not own product lifecycle, product availability, price, stock, category, commission, storefront visibility, cart eligibility, checkout, or sale authority.
- If eligibility is removed, stale links may resolve attribution context but cannot make a hidden/unpublished/deleted/out-of-stock/non-priced product sellable because public discovery and storefront/cart/checkout re-enter backend product authority.

## Downstream Findings

- Public affiliate product projection requires enabled policy, active category, active tenant, current product projection/SQL overlay, storefront URL authority, positive price, and stock availability.
- Public affiliate product API recomposes search results from SQL authority and drops stale candidates.
- Search/recommendation/projection documents remain derivative and cannot preserve eligibility after backend authority says no.

## Failure Findings

- Concurrent product lifecycle/policy changes converge through backend reads, projection fences, and public recomposition.
- Timeout-after-commit ambiguity is handled by backend-read re-entry; frontend success state is not lifecycle authority.
- Partial projection failure can leave stale documents temporarily, but public API recomposition and storefront/cart revalidation fail closed.

## Adversarial Findings

- Forced frontend enablement cannot bypass backend lifecycle/category/commission/price/stock checks.
- Replayed enable/archive/delete requests must re-enter current backend product and policy authority.
- Raw enabled policy rows do not equal effective eligibility when lifecycle/category/price/stock authority is invalid.

## Business Rules Hardening Performed

- Added backend product lifecycle, visibility, price, and stock enablement gate in `AffiliateService`.
- Added effective eligibility recomposition for admin settings reads in `AdminProductAffiliateController`.
- Added active affiliate category authority to public projection and public product recomposition.
- Added regression freeze for lifecycle/category/price/stock/commission boundaries.

## Exact Runtime Files Modified

- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs`
- `PointOfSale.API/Controllers/AdminProductAffiliateController.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductPolicyProjectionConsumer.cs`
- `PointOfSale.API/Controllers/PublicAffiliateProductsController.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Affiliate/AffiliateProductEligibilityLifecycleCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/affiliate-product-eligibility-business-lifecycle-certification-findings.md`

## Lifecycle State Machine

Draft -> Published -> Storefront Visible -> Priced -> In Stock or Backorderable -> Active Affiliate Category -> Valid Commission Policy -> Effective Affiliate Eligible -> Public Affiliate Projection -> Promotion Link Candidate -> Storefront Product Revalidation -> Cart Candidate -> Checkout Revalidation -> Customer Purchase -> Affiliate Attribution -> Commission Pending/Earned -> Delivery Evidence -> Commission Approved -> Withdraw/Payout Request -> Paid.

Forbidden transitions:

- Draft -> Effective Affiliate Eligible.
- Archived -> Effective Affiliate Eligible.
- Deleted -> Effective Affiliate Eligible.
- Hidden -> Public Affiliate Projection.
- Invalid category -> Effective Affiliate Eligible.
- Zero/negative price -> Effective Affiliate Eligible.
- Out of stock without backorder -> Public Affiliate Projection.
- Invalid commission -> Commission Policy.
- Promotion Link -> Customer Purchase without storefront/cart/checkout product authority revalidation.

## Explicit Certification Questions

- Who owns Affiliate Eligibility Rules? Backend.
- Can Draft products become Affiliate Eligible? No.
- Can Archived products remain eligible? No effective eligibility.
- Can Deleted products remain eligible? No.
- Can Hidden products remain eligible? No effective eligibility or public availability.
- Can invalid categories remain eligible? No effective/public eligibility.
- Can invalid commission remain eligible? No.
- Can stale Promotion Links remain valid? They can remain referral candidates only; they cannot bypass lifecycle or sell unavailable products.
- Can lifecycle bypass occur? No inspected runtime bypass remains after hardening.
- Were runtime changes required? Yes.
- Is Affiliate Product Eligibility Business Lifecycle production-certifiable? Yes, for the inspected runtime scope.

## Verification

- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~AffiliateProductEligibilityLifecycleCertificationTests"`
- Result: Passed, 6 tests.

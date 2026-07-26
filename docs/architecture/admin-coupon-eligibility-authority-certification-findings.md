# Admin Coupon Eligibility Authority Certification Findings

Date: 2026-07-01

Scope: PointOfSale.Web Coupon Management runtime for coupon eligibility configuration and presentation, plus directly consumed backend coupon APIs and SQL catalog validation.

Finding: production-certifiable after backend eligibility admission hardening.

Closed risk: admin coupon create/update accepted backend-shaped product/category eligibility JSON after parse validation, but did not prove every product/category ID existed in the current tenant SQL catalog before persistence. A forged DTO could therefore store a non-existent or foreign product/category candidate as accepted coupon eligibility evidence.

Hardening performed:
- `CouponsController.CreateCoupon` and `UpdateCoupon` now route eligibility candidates through backend `ValidateAndCanonicalizeCouponEligibilityAsync` before persistence or outbox event publication.
- Backend parses and canonicalizes applicable product IDs, applicable category IDs, and excluded product IDs.
- Backend rejects empty GUIDs, malformed JSON, product IDs that are both applicable and excluded, foreign/non-existent/deleted products, and foreign/non-existent/deleted categories.
- Accepted eligibility exists only after backend tenant/session authority, Owner/Admin/Manager authorization, SQL catalog validation, coupon business validation, RowVersion/If-Match validation for update, SQL persistence, outbox eventing, accepted `CouponDto`, and frontend DTO admission.
- Regression freezes: `PointOfSale.Tests/Architecture/AdminCouponEligibilityAuthorityCertificationTests.cs` and `PointOfSale.Tests/DiscountValidationHardeningTests.cs`.

Authority rule: Coupon Eligibility is backend-owned. Frontend Coupon Management may search, select, edit, submit eligibility intent, refresh, retry, and render admitted backend DTOs only. React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, Mongo projections, stale DTO replay, modified eligibility JSON, injected product/category IDs, minimum-order edits, currency edits, tenant-scope edits, usage-restriction edits, refresh, reconnect, restart, Redis flush, Mongo rebuild, and projection rebuild must never fabricate accepted eligibility.

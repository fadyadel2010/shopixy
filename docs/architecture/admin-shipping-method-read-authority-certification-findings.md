# PASS SF-SHIP.READ Shipping Method Read Authority Certification

Date: 2026-07-03

Scope reviewed only the Shipping Method read runtime: PointOfSale.Web Shipping Method Management read presentation, Storefront checkout shipping selector/list/summary presentation, backend Shipping Method read APIs, and backend DTOs directly consumed by Storefront.

Shipping Method Read is backend-owned and production-certifiable after read-boundary hardening. Backend tenant-owned SQL `ShippingMethods`, trusted tenant context, admin read endpoints, Storefront tenant/cart resolution, Mongo projection consumers that re-fetch SQL authority, tenant-keyed Redis cache admission, backend currency resolution, backend price calculation, and accepted DTO emission own Shipping Method Identity, Code/Carrier metadata where modeled, Carrier, Enabled State, Visibility, Currency, Estimated Delivery, Display Order, Zone Assignment, Availability, Tenant Scope, Read Authority, and SQL Truth.

PointOfSale.Web and PointOfSale.Storefront own only search/filter/sort/page where present, selection candidates, navigation, refresh, retry, presentation, and rendering. Runtime hardening added no-store response semantics to admin and Storefront Shipping Method reads, no-store Storefront/admin read requests, admin read DTO admission, and Storefront DTO admission before Zustand state. Storefront admission rejects missing/malformed identity, duplicate IDs, foreign tenant evidence, invalid currency, invalid money, invalid ETA, and malformed DTO shape before checkout shipping selector or summary presentation.

Caches and projections remain derivative. Redis may serve only tenant-validated DTO evidence, Mongo projection rows are built only after the projection consumer re-fetches SQL `ShippingMethods`, and browser state/localStorage may preserve only the shipping-method ID candidate. Refresh, reconnect, browser reload, restart, Redis flush, Mongo rebuild, projection rebuild, and stale DTO replay must re-enter backend read admission or fail closed; stale Shipping Methods cannot survive recovery as accepted read authority.

Cross-system review found no authority path from Orders, Checkout, Payment, Coupons, Promotions, Smart Offers, Affiliate, CommerceHub, Marketplace, Inventory, Builder, CMS, or Analytics that can overwrite Shipping Method Read Authority. Adjacent domains may consume accepted shipping-method candidates or immutable order snapshots inside their own certified boundaries, but they cannot create, enable, restore, reassign, or distribute accepted Shipping Method Read Authority.

Pipeline:

- Shipping begins at the Storefront/admin shipping-method read request or selected shipping-method ID candidate.
- Authority begins only at backend tenant-scoped SQL/projection/cache read admission.
- DTO admission begins at backend DTO emission and, for Storefront/admin presentation, frontend DTO admission before state.
- SQL Truth begins in tenant-owned SQL `ShippingMethods` and associated tenant/carrier configuration.
- Presentation begins only after accepted backend DTO admission and frontend read-admission checks.

Runtime changes were required and completed for read-cache/DTO hardening. Shipping Method Read is production-certifiable.

Regression freeze: `PointOfSale.Tests/Architecture/AdminShippingMethodReadAuthorityCertificationTests.cs`.

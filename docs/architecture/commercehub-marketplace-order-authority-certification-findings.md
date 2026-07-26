# PASS M2 CommerceHub Marketplace Order Authority Certification Findings

Scope: PASS M2 CommerceHub Marketplace Order Authority

Status: CommerceHub Marketplace Order Authority is production-certifiable for the inspected runtime paths after vendor-operation and sub-order uniqueness hardening.

## Order identity findings

PostgreSQL `CommerceHubMarketplaceOrders`, `CommerceHubSubOrders`, and `CommerceHubSubOrderItems` own marketplace order identity and lineage. Order numbers and idempotency keys are SQL-unique, order/sub-order/item records are immutable except explicit lifecycle/payment/projection fields, and live sub-orders are now SQL-unique per `(MarketplaceOrderId, StoreId)`.

## Creation findings

`CommerceHubCheckoutService` owns creation through authenticated customer authority, customer/idempotency locks, request-hash binding, serializable SQL, current cart/listing/product/variant/category/vendor/payment/shipping/promo/price/stock recomposition, reservation creation, and SQL outbox staging. DTOs, projections, frontend state, route ids, and stale cart snapshots cannot create order authority.

## Vendor-allocation findings

Vendor allocation derives from current SQL listing/store ownership after checkout recomposition. Runtime hardening added a SQL uniqueness boundary preventing duplicate live sub-orders for one marketplace order/vendor pair.

## Sub-order findings

Sub-orders are generated only inside the CommerceHub order lifecycle boundary and point to the SQL parent order. Immutable EF guards block parent reassignment, store reassignment, order-number mutation, and item snapshot mutation after creation.

## Vendor-access findings

Vendor mutation is scoped by trusted tenant context and `CommerceHubOrderManagementService` rejects foreign sub-orders before lifecycle mutation. Customer reads prove SQL customer ownership or signed order-access token before repairing/serving projections.

## Lifecycle findings

Lifecycle truth is SQL-owned by `CommerceHubOrderManagementService`, transition graph validation, serializable row locks, current payment readiness, inventory reservation release/commit, status history, and projection event staging. Runtime hardening now revalidates current vendor governance/marketplace eligibility before vendor order operations.

## Payment-lineage findings

Marketplace order payment fields are guarded behind CommerceHub payment lifecycle authority. Order services consume payment status for readiness and recovery decisions but do not create financial truth.

## Refund-lineage findings

Refund paths remain owned by refund/payment authority. Marketplace order and sub-order status changes from refunds use SQL refund/payment lifecycle boundaries and cannot reassign order ownership or vendor allocation.

## Tenant/vendor-isolation findings

Tenant/vendor isolation is enforced through trusted tenant context, SQL `StoreId` ownership checks, vendor governance authority, affected-store event scope, and SQL-first customer reads. MongoDB order projections are derivative and are repaired from SQL before customer-critical detail responses.

## Concurrency findings

Checkout uses customer and idempotency locks, persisted idempotency records, request-hash binding, serializable SQL, unique order idempotency/order numbers, and inventory reservation idempotency. Runtime hardening adds database-level collapse for duplicate live vendor sub-orders.

## Historical-consistency findings

Historical order snapshots are immutable. Deleted/ineligible vendors cannot mutate existing order operations after the new governance check, while existing SQL lineage remains available for customer, refund, payment, and audit reads.

## Adversarial findings

Order spoofing, duplicate sub-order generation, vendor reassignment, cross-vendor mutation, cross-tenant mutation, stale projection authority, payment-lineage spoofing, refund-lineage spoofing, route tampering, and DTO tampering are blocked on inspected paths by SQL authority, immutability guards, lifecycle scopes, row locks, request-hash idempotency, and projection repair checks.

## Runtime hardening performed

- Added `EnsureVendorOrderOperationAllowedAsync` and called it before vendor sub-order lifecycle mutation.
- Added SQL uniqueness for live `(MarketplaceOrderId, StoreId)` sub-orders.
- Added migration preflight that refuses rollout if duplicate live marketplace-order/vendor sub-orders already exist.

## Exact runtime files modified

- `PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs`
- `PointOfSale.API/Services/Governance/IUserGovernanceService.cs`
- `PointOfSale.API/Services/Governance/UserGovernanceService.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CommerceHubSubOrderConfiguration.cs`
- `PointOfSale.Infrastructure/Migrations/20260610193000_CommerceHubMarketplaceOrderAuthorityHardening.cs`

## Regression freezes added

- `PointOfSale.Tests/Architecture/CommerceHubMarketplaceOrderAuthorityCertificationTests.cs`

## Architecture updates

Updated all five `.ai-context` authority files with PASS M2 ownership, hardening, risk, and freeze rules.

## Required answers

- Who owns marketplace order authority? PostgreSQL `CommerceHubMarketplaceOrders`, `CommerceHubSubOrders`, and `CommerceHubSubOrderItems`, mutated only through backend CommerceHub checkout/lifecycle/payment/refund boundaries.
- Can marketplace orders be replayed? Matching idempotent replay returns the same SQL order; conflicting replay is request-hash/idempotency rejected.
- Can vendor allocation be manipulated? No on inspected paths; allocation derives from current SQL listing/store authority and live parent/vendor sub-orders are unique.
- Can sub-orders diverge from parent orders? No on inspected paths; SQL FK lineage, immutability guards, lifecycle scopes, and the new live uniqueness constraint block divergence.
- Can vendors access foreign orders? No on inspected mutation paths; `StoreId` must match trusted tenant context.
- Can suspended vendors continue order operations? No after M2 hardening; vendor operations revalidate current governance and marketplace eligibility.
- Can payment lineage diverge? No on inspected paths; payment fields are payment-lifecycle-owned and order reads use SQL payment status.
- Can refund lineage diverge? No on inspected paths; refund mutation remains refund/payment-authority-owned and cannot mutate order ownership.
- Can tenant leakage occur? No inspected tenant/vendor/customer path served or mutated foreign SQL authority.
- Can replay corrupt marketplace order authority? No on inspected paths; replay collapses through idempotency, SQL uniqueness, immutable lineage, and projection freshness checks.
- Were runtime code changes required? Yes.
- Is CommerceHub Marketplace Order Authority production-certifiable? Yes, for the inspected runtime authority paths.

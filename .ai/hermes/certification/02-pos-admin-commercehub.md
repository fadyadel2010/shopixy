# Certification Summary

Certification ID: CS-002

Relationship: POS Admin ↔ CommerceHub

Status: PASS

Certification Type: Cross-System Relationship Certification

Certification Standard: `.ai/hermes/CROSS_SYSTEM_CERTIFICATION.md`

Knowledge Base Version: v1.0

The POS Admin ↔ CommerceHub relationship is certified because both participating system guides now describe the same business reality from compatible perspectives. POS Admin is documented as the tenant operational surface, CommerceHub is documented as the shared multi-vendor marketplace, and the embedded CommerceHub Vendor Module is documented as an operational participation surface that submits vendor intent through backend SQL/Core/CommerceHub authority.

The certified relationship preserves the Shopixy authority model: PostgreSQL remains authoritative, backend lifecycle owners admit or reject marketplace-vendor intent, CommerceHub presents admitted marketplace state, and POS Admin consumes vendor-facing marketplace evidence without becoming marketplace authority.

# Scope

This certification validates the architectural relationship between POS Admin and CommerceHub only.

It certifies that the POS Admin system guide and CommerceHub system guide describe their relationship consistently regarding actors, terminology, operational direction, backend authority, marketplace participation, vendor operations, marketplace orders, finance, governance, visibility, ratings/reviews, and QA boundaries.

This certification does not certify implementation behavior, API behavior, UI completeness, runtime production readiness, code quality, database schema, performance, or unrelated systems. It records only the certified Knowledge Base relationship between POS Admin and CommerceHub.

# Certified Relationship

The certified forward operational relationship is:

```text
POS Admin
↓
CommerceHub Vendor Module
↓
Backend SQL/Core/CommerceHub Authority
↓
CommerceHub Marketplace
```

The certified reverse operational relationship is:

```text
CommerceHub Marketplace
↓
Backend SQL/Core/CommerceHub Authority
↓
CommerceHub Vendor Module
↓
POS Admin Tenant Vendor Operator
```

POS Admin owns tenant operations. It is the tenant back office and point-of-sale surface for tenant catalog, pricing, inventory, purchasing, sales, returns, accounting reads, tenant settings, Storefront inputs, tenant communication, and tenant-scoped operations. POS Admin does not become CommerceHub itself and does not own marketplace authority.

The CommerceHub Vendor Module is an embedded operational participation surface inside POS Admin. It allows a POS tenant to participate as a CommerceHub vendor, review vendor-facing marketplace state, submit publication or fulfillment intent where allowed, view vendor earnings and finance evidence, and respond to marketplace recovery or governance requirements. The Vendor Module is not marketplace authority, not Marketplace governance, not tenant accounting, not POS payment-at-sale authority, and not Storefront order authority.

Backend SQL/Core/CommerceHub authority admits or rejects vendor intent. Product publication, listing visibility, marketplace order/sub-order state, payment finality, fulfillment mutation, inventory side effects, settlement, payout readiness, COD debt, refund effects, ratings/reviews admission, and governance outcomes are all admitted through the appropriate backend authority and lifecycle boundaries.

CommerceHub presents admitted marketplace state. It exposes marketplace discovery, listings, cart/checkout, marketplace orders, sub-orders, payment evidence, fulfillment evidence, vendor finance evidence, governance state, ratings/reviews, notifications, search, and discovery only after backend authority admits the relevant state.

Reverse marketplace evidence flows back through backend authority before reaching POS Admin. Marketplace order/sub-order evidence, finance evidence, governance evidence, visibility evidence, settlement evidence, payout readiness, ratings/reviews, notifications, and recovery state must pass through backend SQL/Core/CommerceHub authority before the CommerceHub Vendor Module presents them to the POS Admin tenant-vendor operator.

# Certified Architectural Areas

The POS Admin and CommerceHub guides are certified as consistent for the following areas:

- Vendor Module: the embedded CommerceHub Vendor Module is an operational participation surface inside POS Admin, not marketplace authority.
- Marketplace Participation: POS tenants may participate in CommerceHub as vendors through admitted backend authority.
- Marketplace Publication: POS Admin submits publication intent; backend SQL/Core/CommerceHub authority admits or rejects marketplace listing creation and visibility.
- Marketplace Visibility: CommerceHub visibility derives from current SQL listing, product, variant, category, vendor, tenant, price, stock, and governance authority.
- Vendor Eligibility: vendor participation depends on current backend identity, tenant, eligibility, and governance authority.
- Vendor Governance: marketplace governance remains a CommerceHub/Super Admin platform-governance concern and does not become POS Admin tenant-business authority.
- Vendor Suspension: suspension affects CommerceHub marketplace participation, visibility, and new marketplace-order eligibility; it does not transfer ownership of the POS Admin tenant business.
- Vendor Capability: vendor capabilities are admitted through backend authority and current governance state; UI or module visibility does not grant authority.
- Marketplace Orders: CommerceHub owns the marketplace order model through backend SQL authority.
- Marketplace Sub-Orders: vendor-facing sub-orders are scoped by vendor/StoreId and are not generic POS or Storefront orders.
- Generic Orders: generic POS/Storefront orders remain separate from CommerceHub marketplace orders and sub-orders.
- Order Separation: marketplace orders/sub-orders must not be treated as POS sales invoices or Storefront orders.
- Inventory: tenant inventory authority remains SQL/Core inventory authority; CommerceHub consumes current inventory evidence and cannot treat projections as stock truth.
- Pricing: POS Admin provides tenant product/pricing inputs, but CommerceHub marketplace admission revalidates pricing through backend authority.
- Marketplace Currency: CommerceHub marketplace financial authority is EGP-only and remains separate from tenant/storefront currency authority.
- Settlement: settlement authority remains SQL/ledger-backed and depends on current settlement-order lineage.
- Payout Readiness: vendor payouts require backend readiness gates, ledger evidence, external-money safety, and lifecycle admission.
- Vendor Earnings: vendor earnings are CommerceHub marketplace finance evidence, not tenant accounting truth.
- COD: COD debt and COD risk controls are CommerceHub marketplace finance/governance concerns and can affect marketplace visibility and new-order admission.
- Online Payments: CommerceHub payment paths are marketplace-specific; provider callbacks and external provider results remain evidence until backend lifecycle admission.
- Accounting: tenant accounting and CommerceHub marketplace finance remain separate; ledger authority remains backend SQL authority.
- Finance: CommerceHub finance covers marketplace earnings, COD debt, settlement, payout readiness, refund effects, and reconciliation; it does not become POS tenant treasury.
- Shipping: marketplace split-order shipping and sub-order shipping evidence remain tied to CommerceHub marketplace order/sub-order authority.
- Fulfillment: POS Admin Vendor Module submits fulfillment/cancellation/delivery intent; backend CommerceHub order-management/lifecycle authority owns mutation admission.
- Search: CommerceHub search is derivative and filtered through SQL authority overlays and governance; search never becomes visibility authority.
- Discovery: marketplace discovery presents admitted listing/vendor/product evidence and remains subordinate to SQL/Core/CommerceHub authority.
- Marketplace Ratings: ratings are marketplace trust evidence admitted through CommerceHub governance/moderation authority and do not become commerce or finance authority.
- Marketplace Reviews: reviews and vendor responses are marketplace trust/moderation evidence; they cannot decide order, payment, fulfillment, payout, stock, pricing, COD debt, settlement, or tenant accounting truth.
- Notifications: notifications are derivative outputs of admitted lifecycle/governance events and cannot mutate business truth.
- Authority Boundaries: POS Admin, CommerceHub, Super Admin, Core lifecycle owners, and derivative systems retain separate authority boundaries.
- Business Rules: both guides preserve the same durable business rules for vendor participation, marketplace operation, finance separation, and fail-closed authority.
- Never Assume: both guides warn against treating UI state, projections, provider evidence, ratings/reviews, notifications, or dashboards as authority.
- QA Checklist: both guides direct QA to identify authority owners, distinguish evidence from truth, preserve tenant/vendor scope, and verify backend admission before judging behavior.

# Authority Consistency

The certified relationship preserves these shared architectural truths:

- SQL remains authoritative for durable Shopixy business truth.
- CommerceHub Vendor Module never becomes marketplace authority.
- Presentation never becomes authority.
- Evidence never becomes authority.
- Vendor operational intent always passes through backend lifecycle validation.
- Marketplace governance remains separate from tenant operations.
- POS Admin owns tenant operations, not CommerceHub marketplace governance.
- CommerceHub owns marketplace orchestration and presentation over backend authority, not POS tenant business operations.
- Core lifecycle owners remain the mutation authorities for protected payment, order, fulfillment, inventory, refund, settlement, payout, ledger, identity, tenant, and governance state.
- MongoDB, Redis, Elasticsearch, browser state, notifications, analytics, provider callbacks, ratings/reviews, dashboards, and UI surfaces remain derivative evidence or presentation surfaces.
- Super Admin governance applies to CommerceHub marketplace participation and platform operations; it does not manage the whole POS Admin tenant business.

# Certification Notes

Future reviewers must preserve these architectural decisions:

- CommerceHub Vendor Module terminology is certified as the embedded POS Admin vendor operational surface. CommerceHub may also refer to POS Admin vendor surfaces, but the two terms must remain explicitly mapped to the same operational surface.
- The forward operational flow must remain: POS Admin → CommerceHub Vendor Module → Backend SQL/Core/CommerceHub Authority → CommerceHub Marketplace.
- The reverse operational flow must remain: CommerceHub Marketplace → Backend SQL/Core/CommerceHub Authority → CommerceHub Vendor Module → POS Admin Tenant Vendor Operator.
- Tenant operations and marketplace governance must remain separate. POS Admin tenant-business authority must not be absorbed into CommerceHub or Super Admin governance.
- Marketplace ratings and reviews must remain trust/moderation evidence only. They must not become product, price, stock, checkout, payment, fulfillment, payout, COD-debt, settlement, or tenant-accounting authority.
- Super Admin governance boundaries must remain intact. Super Admin may govern marketplace participation, vendor visibility, marketplace integrity, emergency operations, and payout execution workflows where authorized, but it must not bypass backend lifecycle owners or manage the whole POS Admin tenant business.
- Marketplace orders/sub-orders and generic POS/Storefront orders must remain separate authority models.
- Marketplace finance, vendor earnings, settlement, payout readiness, COD debt, and marketplace payment evidence must remain separate from tenant accounting, tenant treasury, Storefront payment methods, and POS payment-at-sale configuration.

# Future Impact

Future modifications to either the POS Admin guide or CommerceHub guide must preserve this certified relationship.

Changes affecting any of the following areas require re-certification of the POS Admin ↔ CommerceHub relationship:

- Vendor Module
- Marketplace Governance
- Publication
- Visibility
- Settlement
- Ratings
- Reviews
- Authority Boundaries
- Vendor eligibility or capability
- Vendor suspension or ban behavior
- Marketplace orders or sub-orders
- Generic order separation
- Marketplace finance, payout readiness, vendor earnings, COD debt, or accounting separation
- Marketplace payment evidence or provider-finality wording
- Search, discovery, notifications, or derivative presentation surfaces where they could be mistaken for authority
- Super Admin governance boundaries involving POS Admin or CommerceHub

Re-certification is required when a future change could alter actor powers, authority direction, terminology meaning, backend admission requirements, cross-system ownership, or evidence-versus-authority boundaries.

# References

Certification Standard:

- `.ai/hermes/CROSS_SYSTEM_CERTIFICATION.md`

Primary System Guides:

- `.ai/hermes/systems/pos-admin/README.md`
- `.ai/hermes/systems/commercehub/README.md`

Relevant Core Guides:

- `.ai/hermes/INDEX.md`
- `.ai/hermes/02-architecture.md`
- `.ai/hermes/03-inventory.md`
- `.ai/hermes/04-payments.md`
- `.ai/hermes/05-orders.md`
- `.ai/hermes/06-security-identity.md`
- `.ai/hermes/07-platform-overview.md`
- `.ai/hermes/08-qa-philosophy.md`
